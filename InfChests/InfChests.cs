using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Terraria;
using Terraria.IO;
using TerrariaApi.Server;
using TShockAPI;
using System.Linq;

namespace InfChests
{
	[ApiVersion(1, 23)]
	public class InfChests : TerrariaPlugin
	{
		#region Plugin Info
		public override string Name { get { return "InfiniteChests"; } }
		public override string Author { get { return "Zaicon"; } }
		public override string Description { get { return "A server-sided chest manager."; } }
		public override Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

		public InfChests(Main game)
			: base(game)
		{

		}
		#endregion

		#region Init/Dispose
		public override void Initialize()
		{
			ServerApi.Hooks.GameInitialize.Register(this, onInitialize);
			ServerApi.Hooks.GamePostInitialize.Register(this, onWorldLoaded);
			ServerApi.Hooks.NetGetData.Register(this, onGetData);
			ServerApi.Hooks.ServerLeave.Register(this, onLeave);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				ServerApi.Hooks.GameInitialize.Deregister(this, onInitialize);
				ServerApi.Hooks.GamePostInitialize.Deregister(this, onWorldLoaded);
				ServerApi.Hooks.NetGetData.Deregister(this, onGetData);
				ServerApi.Hooks.ServerLeave.Deregister(this, onLeave);
			}
			base.Dispose(disposing);
		}
		#endregion

		internal static Dictionary<int, Data> playerData = new Dictionary<int, Data>();
		internal static bool lockChests = false;
		internal static bool notInfChests = false;
		private static Dictionary<int, RefillInfo> refillInfo = new Dictionary<int, RefillInfo>();

		#region Hooks
		private void onInitialize(EventArgs args)
		{
			DB.Connect();
			for (int i = 0; i < TShock.Players.Length; i++)
			{
				playerData.Add(i, new Data(i));
			}

			Commands.ChatCommands.Add(new Command("ic.use", ChestCMD, "chest"));
			Commands.ChatCommands.Add(new Command("ic.convert", ConvChests, "convchests"));
			Commands.ChatCommands.Add(new Command("ic.prune", PruneChests, "prunechests"));
		}

		private async void onWorldLoaded(EventArgs args)
		{
			lockChests = true;
			await Task.Factory.StartNew(() => convertChests());
			lockChests = false;
		}

		private async void onGetData(GetDataEventArgs args)
		{
			if (notInfChests)
				return;

			int index = args.Msg.whoAmI;

			using (var reader = new BinaryReader(new MemoryStream(args.Msg.readBuffer, args.Index, args.Length)))
			{
				switch (args.MsgID)
				{
					case PacketTypes.ChestGetContents: //31 GetContents
						args.Handled = true;

						short tilex = reader.ReadInt16();
						short tiley = reader.ReadInt16();

						//if player opens a new chest (nearby) without closing first chest
						if (playerData[index].dbid != -1)
						{
							if (playerData[index].lockChests)
								return;

							playerData[index].lockChests = true;

							await Task.Factory.StartNew(() =>
							{
								
								int oldChestID = playerData[index].dbid;
								Item[] oldChestItems = (Item[])playerData[index].oldChestItems.Clone();
								Item[] newChestItems = (Item[])playerData[index].newChestItems.Clone();

								for (int i = 0; i < 40; i++)
								{
									if (oldChestItems[i] != newChestItems[i])
									{
										DB.setItem(oldChestID, newChestItems[i], i);
									}
								}
							});

							playerData[index].lockChests = false;

							playerData[index].dbid = -1;
							playerData[index].oldChestItems = new Item[40];
							playerData[index].newChestItems = new Item[40];
							NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, index, "", index, -1);
						}

						if (lockChests)
							TShock.Players[index].SendWarningMessage("Chest conversion in progress. Please wait.");
						else if (!playerData[index].lockChests)
							await Task<bool>.Factory.StartNew(() => getChestContents(args.Msg.whoAmI, tilex, tiley));
						else
							TShock.Players[index].SendErrorMessage("Please try again shortly.");
#if DEBUG
						File.AppendAllText("debug.txt", $"31 ChestGetContents | WhoAmI: {args.Msg.whoAmI} | tilex = {tilex} | tiley = {tiley}\n");
#endif
						break;
					case PacketTypes.ChestItem: //22 ChestItem
						args.Handled = true;

						if (lockChests)
						{
							TShock.Players[index].SendWarningMessage("Chest conversion in progress. Please wait.");
							args.Handled = true;
							return;
						}
						short chestid = reader.ReadInt16();
						byte itemslot = reader.ReadByte();
						short stack = reader.ReadInt16();
						byte prefix = reader.ReadByte();
						short itemid = reader.ReadInt16();

#if DEBUG
						if (itemslot == 0 || itemslot == 39)
							File.AppendAllText("debug.txt", $"22 ChestItem | WhoAmI: {args.Msg.whoAmI} | chestid = {chestid} | slot = {itemslot} | stack = {stack} | prefix = {prefix} | itemid = {itemid}\n");
#endif

						//If someone sends this packet manually
						if (playerData[index].dbid == -1)
							break;

						await Task.Factory.StartNew(() => {

							playerData[index].newChestItems[itemslot] = new Item();
							playerData[index].newChestItems[itemslot].SetDefaults(itemid);
							playerData[index].newChestItems[itemslot].stack = stack;
							playerData[index].newChestItems[itemslot].prefix = prefix;

							if (refillInfo.ContainsKey(playerData[index].dbid))
								refillInfo[playerData[index].dbid].items[itemslot] = playerData[index].newChestItems[itemslot].Clone();
							
						});

						break;
					case PacketTypes.ChestOpen: //33 SetChestName
						args.Handled = true;

						chestid = reader.ReadInt16();
						tilex = reader.ReadInt16();
						tiley = reader.ReadInt16();
						if (Main.tile[tilex, tiley].frameY % 36 != 0)
							tiley--;
						if (Main.tile[tilex, tiley].frameX % 36 != 0)
							tilex--;
						if (reader.ReadByte() > 0) //if we get chest rename, ignore
							break;
#if DEBUG
						File.AppendAllText("debug.txt", $"33 ChestOpen | WhoAmI: {args.Msg.whoAmI} | chestid = {chestid} | tilex = {tilex} | tiley = {tiley}\n");
#endif

						if (chestid == -1 && playerData[index].dbid != -1)
						{
							NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, index, "", index, -1);
							playerData[index].lockChests = true;

							await Task.Factory.StartNew(() => {

								if (DB.isRefill(playerData[index].dbid))
									return;

								int oldChestID = playerData[index].dbid;
								Item[] oldChestItems = (Item[])playerData[index].oldChestItems.Clone();
								Item[] newChestItems = (Item[])playerData[index].newChestItems.Clone();

								for (int i = 0; i < 40; i++)
								{
									if (oldChestItems[i] != newChestItems[i])
									{
										DB.setItem(oldChestID, newChestItems[i], i);
									}
								}
								
							});

							playerData[index].lockChests = false;
							playerData[index].dbid = -1;
							playerData[index].oldChestItems = new Item[40];
							playerData[index].newChestItems = new Item[40];
						}
						else if (chestid == -1 || chestid == -3 || chestid == -2) // -2 is open piggy bank, -3 is open safe, -1 is close piggy bank/safe
						{
							//do nothing
						}
						else 
						{
							args.Handled = true;
							TShock.Log.ConsoleError("Unhandled ChestOpen packet.");
							TShock.Log.Info($"ChestID: {chestid} | X: {tilex} | Y: {tiley}");
						}
						break;
					case PacketTypes.TileKill:
						if (lockChests)
						{
							TShock.Players[index].SendWarningMessage("Chest conversion in progress. Please wait.");
							args.Handled = true;
							return;
						}
						
						byte action = reader.ReadByte(); // 0 placechest, 1 killchest, 2 placedresser, 3 killdresser
						tilex = reader.ReadInt16();
						tiley = reader.ReadInt16();
						short style = reader.ReadInt16();
						int chestnum = -1;

#if DEBUG
						File.AppendAllText("debug.txt", $"34 TileKill | WhoAmI: {args.Msg.whoAmI} | action = {action} | tilex = {tilex} | tiley = {tiley} | style = {style}\n");
#endif
						if (playerData[index].lockChests)
						{
							args.Handled = true;
							TShock.Players[index].SendTileSquare(tilex, tiley, 3);
							return;
						}

						if (action == 0 || action == 2)
						{
							if (TShock.Regions.CanBuild(tilex, tiley, TShock.Players[index]))
							{
								playerData[index].lockChests = true;
								chestnum = WorldGen.PlaceChest(tilex, tiley, type: action == 0 ? (ushort)21 : (ushort)88, style: style);
								if (chestnum == -1)
									break;
								foreach (TSPlayer plr in TShock.Players.Where(p => p != null && p.Active))
									plr.SendTileSquare(Main.chest[chestnum].x, Main.chest[chestnum].y, 3);

								DB.addChest(new InfChest()
								{
									items = Main.chest[chestnum].item,
									userid = TShock.Players[index].HasPermission("ic.protect") ? TShock.Players[index].User.ID : -1,
									x = Main.chest[chestnum].x,
									y = Main.chest[chestnum].y
								});
								Main.chest[chestnum] = null;
								playerData[index].lockChests = false;
								if (TShock.Players[index].HasPermission("ic.protect"))
									TShock.Players[index].SendInfoMessage("This chest has been automatically protected under your account.");
							}
							args.Handled = true;
						}
						else
						{
							if (TShock.Regions.CanBuild(tilex, tiley, TShock.Players[index]) && (Main.tile[tilex, tiley].type == 21 || Main.tile[tilex, tiley].type == 88))
							{
								if (Main.tile[tilex, tiley].frameY % 36 != 0)
									tiley--;
								if (Main.tile[tilex, tiley].frameX % 36 != 0)
									tilex--;

								InfChest chest2 = DB.getChest(tilex, tiley);
								TSPlayer player = TShock.Players[index];
								if (chest2 == null)
								{
									WorldGen.KillTile(tilex, tiley);
								}
								else if (chest2.userid != -1 && !player.HasPermission("ic.edit") && chest2.userid != player.User.ID)
								{
									player.SendErrorMessage("This chest is protected.");
								}
								else if (chest2.items.Any(p => p.type != 0))
								{
									//Do nothing - ingore tilekill attempt when items are in chest
								}
								else if (chest2.refillTime > 0)
								{
									player.SendErrorMessage("You cannot destroy refilling chests.");
								}
								else
								{
									WorldGen.KillTile(tilex, tiley);
									DB.removeChest(chest2.id);
								}
								foreach (TSPlayer plr in TShock.Players.Where(p => p != null && p.Active))
									plr.SendTileSquare(tilex, tiley, 3);
								args.Handled = true;
							}
						}
						break;
					case PacketTypes.ChestName:
#if DEBUG
						File.AppendAllText("debug.txt", $"69 ChestName | WhoAmI: {args.Msg.whoAmI}\n");
#endif
						//Do nothing - we don't handle chest name
						args.Handled = true;
						break;
					case PacketTypes.ForceItemIntoNearestChest:
						args.Handled = true;

						if (lockChests)
							return;

						byte invslot = reader.ReadByte();

#if DEBUG
						File.AppendAllText("debug.txt", $"85 ForceItem | WhoAmI: {args.Msg.whoAmI} | invslot {invslot}\n");
#endif

						//At the moment, we only allow quickstacking for chest owners & users with edit perm & users with correct password & non-refilling chests
						if (TShock.Players[index].IsLoggedIn)
						{
							playerData[index].slotQueue.Add(invslot);
							if (playerData[index].slotQueue.Count == 1)
							{
								playerData[index].quickStackTimer.Start();
								playerData[index].location = new Point(TShock.Players[index].TileX, TShock.Players[index].TileY);
							}
						}
						break;
				}
			}
		}

		private void onLeave(LeaveEventArgs args)
		{
			playerData[args.Who] = new Data(args.Who);
		}
		#endregion

		private bool getChestContents(int index, short tilex, short tiley)
		{
			InfChest chest = DB.getChest(tilex, (short)(tiley));
			TSPlayer player = TShock.Players[index];

			if (chest == null)
			{
				WorldGen.KillTile(tilex, tiley);
				TSPlayer.All.SendData(PacketTypes.Tile, "", 0, tilex, tiley + 1);
				player.SendWarningMessage("This chest was corrupted.");
				playerData[index].action = chestAction.none;
				return true;
			}

			if (playerData.Values.Any(p => p.dbid == chest.id))
			{
				player.SendErrorMessage("This chest is in use.");
				playerData[index].action = chestAction.none;
				
				player.SendData(PacketTypes.ChestOpen, "", -1);

				return true;
			}

			switch (playerData[index].action)
			{
				case chestAction.info:
					player.SendInfoMessage($"X: {chest.x} | Y: {chest.y}");
					string owner = chest.userid == -1 ? "(None)" : TShock.Users.GetUserByID(chest.userid).Name;
					string ispublic = chest.isPublic ? " (Public)" : "";
					string isrefill = chest.refillTime > 0 ? $" (Refill: {chest.refillTime})" : "";
					player.SendInfoMessage($"Chest Owner: {owner}{ispublic}{isrefill}");
					if (chest.groups.Count > 0)
					{
						string info = string.Join(", ", chest.groups);
						player.SendInfoMessage($"Groups Allowed: {info}");
					}
					else
						player.SendInfoMessage("Groups Allowed: (None)");
					if (chest.users.Count > 0)
					{
						string info = string.Join(", ", chest.users.Select(p => TShock.Users.GetUserByID(p).Name));
						player.SendInfoMessage($"Users Allowed: {info}");
					}
					else
						player.SendInfoMessage("Users Allowed: (None)");
					break;
				case chestAction.protect:
					if (chest.userid == player.User.ID)
						player.SendErrorMessage("This chest is already claimed by you!");
					else if (chest.userid != -1 && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is already claimed by someone else!");
					else
					{
						if (DB.setUserID(chest.id, player.User.ID))
							player.SendSuccessMessage("This chest is now claimed by you!");
						else
						{
							player.SendErrorMessage("An error occured.");
							TShock.Log.Error("Error setting chest protection.");
						}
					}
					break;
				case chestAction.unProtect:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is not yours!");
					else if (chest.userid == -1)
						player.SendErrorMessage("This chest is not claimed!");
					else
					{
						if (DB.setUserID(chest.id, -1) && DB.setRefill(chest.id, 0))
							player.SendSuccessMessage("This chest is no longer claimed.");
						else
						{
							player.SendErrorMessage("An error occured.");
							TShock.Log.Error("Error setting chest un-protection.");
						}
					}
					break;
				case chestAction.allowUser:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is not yours!");
					else if (chest.userid == -1)
						player.SendErrorMessage("This chest is not claimed!");
					else
					{
						if (chest.users.Contains(playerData[index].userIDToChange))
							player.SendErrorMessage("That user already has access to this chest!");
						else
						{
							chest.users.Add(playerData[index].userIDToChange);
							DB.setUsers(chest.id, chest.users);
							player.SendSuccessMessage("Added user to chest.");
						}
					}
					break;
				case chestAction.removeUser:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is not yours!");
					else if (chest.userid == -1)
						player.SendErrorMessage("This chest is not claimed!");
					else
					{
						if (!chest.users.Contains(playerData[index].userIDToChange))
							player.SendErrorMessage("That user does not have access to this chest.");
						else
						{
							chest.users.Remove(playerData[index].userIDToChange);
							DB.setUsers(chest.id, chest.users);
							player.SendSuccessMessage("Removed user to chest.");
						}
					}
					break;
				case chestAction.allowGroup:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is not yours!");
					else if (chest.userid == -1)
						player.SendErrorMessage("This chest is not claimed!");
					else
					{
						if (chest.groups.Contains(playerData[index].groupToChange))
							player.SendErrorMessage("That group already has access to this chest!");
						else
						{
							chest.groups.Add(playerData[index].groupToChange);
							DB.setGroups(chest.id, chest.groups);
							player.SendSuccessMessage("Added group to chest.");
						}
					}
					break;
				case chestAction.removeGroup:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is not yours!");
					else if (chest.userid == -1)
						player.SendErrorMessage("This chest is not claimed!");
					else
					{
						if (!chest.groups.Contains(playerData[index].groupToChange))
							player.SendErrorMessage("That group does not have access to this chest.");
						else
						{
							chest.groups.Remove(playerData[index].groupToChange);
							DB.setGroups(chest.id, chest.groups);
							player.SendSuccessMessage("Removed group from chest.");
						}
					}
					break;
				case chestAction.togglePublic:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is not yours!");
					else if (chest.userid == -1)
						player.SendErrorMessage("This chest is not claimed!");
					else
					{
						if (!DB.setPublic(chest.id, !chest.isPublic))
						{
							player.SendErrorMessage("An error occured.");
							TShock.Log.Error("Error toggling chest protection.");
							break;
						}
						if (chest.isPublic)
							player.SendSuccessMessage("This chest is now private.");
						else
							player.SendSuccessMessage("This chest is now public.");
					}
					break;
				case chestAction.setRefill:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is not yours!");
					else if (chest.userid == -1)
						player.SendErrorMessage("This chest is not claimed!");
					else
					{
						if (!DB.setRefill(chest.id, playerData[player.Index].refillTime))
						{
							player.SendErrorMessage("An error occured.");
							TShock.Log.Error("Error setting chest refill.");
						}
						else
						{
							if (playerData[player.Index].refillTime == 0)
								player.SendSuccessMessage("Removed this chest's auto-refill.");
							else
								player.SendSuccessMessage("Set refill time to " + playerData[player.Index].refillTime + " seconds.");

							if (refillInfo.ContainsKey(chest.id))
								refillInfo.Remove(chest.id);
						}
					}
					break;
				case chestAction.setName:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit"))
						player.SendErrorMessage("This chest is not yours!");
					else if (chest.userid == -1)
						player.SendErrorMessage("This chest is not claimed!");
					else
					{
						if (!DB.setName(chest.id, playerData[index].chestName))
						{
							player.SendErrorMessage("An error occured.");
							TShock.Log.Error("Error setting chest name.");
						}
						else
							player.SendSuccessMessage("Saved chest as '" + playerData[index].chestName + "'.");
					}
					break;
				case chestAction.loadName:
					if (chest.userid != player.User.ID && !player.HasPermission("ic.edit") && chest.userid != -1)
						player.SendErrorMessage("This chest is not yours!");
					else
					{
						if (DB.setAll(chest.id, DB.getChest(playerData[index].chestName)))
							player.SendSuccessMessage("Chest loaded from chest '" + playerData[index].chestName + "'.");
						else
						{
							player.SendErrorMessage("An error occured.");
							TShock.Log.Error("Error loading chest from name.");
						}
					}
					break;
				case chestAction.none:
					if (chest.userid != -1 && !player.IsLoggedIn && !chest.isPublic && !chest.groups.Contains(TShock.Config.DefaultGuestGroupName))
						player.SendErrorMessage("You must be logged in to use this chest.");
					else if (player.IsLoggedIn && !chest.isPublic && chest.userid != -1 && !player.HasPermission("ic.edit") && chest.userid != player.User.ID && !chest.users.Contains(player.User.ID) && !chest.groups.Contains(player.Group.Name))
						player.SendErrorMessage("This chest is protected.");
					else
					{
						//int chestindex = -1;
						//for (int i = 0; i < Main.chest.Length; i++)
						//{
						//	if (Main.chest[i] == null)
						//	{
						//		chestindex = i;
						//		break;
						//	}
						//}
						//if (chestindex == -1)
						//{
						//	player.SendErrorMessage("An error occured.");
						//	TShock.Log.ConsoleError("Error: No empty chests available.");
						//	break;
						//}

						playerData[index].dbid = chest.id;
						playerData[index].oldChestItems = (Item[])chest.items.Clone();
						playerData[index].newChestItems = (Item[])chest.items.Clone();
						//playerData[index].mainid = chestindex;

						Item[] writeItems;

						if (chest.refillTime > 0)
						{

							if (refillInfo.ContainsKey(chest.id))
							{
								//int cindex = refillInfo.FindIndex(p => p.Item1 == chest.id);
								if ((DateTime.Now - refillInfo[chest.id].lastView).TotalSeconds > chest.refillTime)
								{
									refillInfo[chest.id].items = (Item[])chest.items.Clone();
									writeItems = refillInfo[chest.id].items;
									refillInfo[chest.id].lastView = DateTime.Now;
									TShock.Players[index].SendWarningMessage("This chest will refill in " + chest.refillTime + " seconds!");
								}
								else
								{
									writeItems = refillInfo[chest.id].items;
									int time = chest.refillTime - (DateTime.Now - refillInfo[chest.id].lastView).Seconds;
									TShock.Players[index].SendWarningMessage("This chest will refill in " + time + " seconds!");
								}
							}
							else
							{
								writeItems = chest.items;
								refillInfo.Add(chest.id, new RefillInfo((Item[])chest.items.Clone(), DateTime.Now));
								TShock.Players[index].SendWarningMessage("This chest refills every " + chest.refillTime + " seconds!");
							}
						}
						else
							writeItems = chest.items;

						Main.chest[0] = new Chest()
						{
							item = writeItems,
							x = chest.x,
							y = chest.y
						};

						for (int i = 0; i < writeItems.Length; i++)
						{
							player.SendData(PacketTypes.ChestItem, "", 0, i, writeItems[i].stack, writeItems[i].prefix, writeItems[i].type);
						}
						player.SendData(PacketTypes.ChestOpen, "", 0, chest.x, chest.y);
						try
						{
							NetMessage.SendData((int)PacketTypes.SyncPlayerChestIndex, -1, index, "", index, 0);
						}
						catch (Exception ex)
						{
							TShock.Log.Error("NetMessage.SendData(syncplayerchestindex) Error: " + ex.Message);
						}

						Main.chest[0] = null;
					}
					break;
			}
			playerData[index].action = chestAction.none;

			return true;
		}

		#region Chest Commands
		private void ChestCMD(CommandArgs args)
		{
			if (notInfChests)
			{
				args.Player.SendErrorMessage("InfiniteChests are not enabled on this server.");
				return;
			}

			if (args.Parameters.Count == 0 || args.Parameters[0].ToLower() == "help")
			{
				List<string> help = new List<string>();

				args.Player.SendErrorMessage("Invalid syntax:");
				if (args.Player.HasPermission("ic.claim"))
					help.Add("/chest <claim/unclaim>");
				if (args.Player.HasPermission("ic.info"))
					help.Add("/chest info");
				if (args.Player.HasPermission("ic.search"))
					help.Add("/chest search <item name>");
				if (args.Player.HasPermission("ic.claim"))
				{
					help.Add("/chest allow <player name>");
					help.Add("/chest remove <player name>");
					help.Add("/chest allowgroup <group name>");
					help.Add("/chest removegroup <group name>");
				}
				if (args.Player.HasPermission("ic.public"))
					help.Add("/chest public");
				if (args.Player.HasPermission("ic.refill"))
					help.Add("/chest refill <seconds>");
				if (args.Player.HasPermission("ic.save"))
				{
					help.Add("/chest save <chest name>");
					help.Add("/chest load <chest name>");
				}
				help.Add("/chest cancel");

				int pageNumber;
				if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
					return;

				PaginationTools.SendPage(args.Player, pageNumber, help, new PaginationTools.Settings() { HeaderFormat = "Chest Subcommands ({0}/{1}):", FooterFormat = "Type /chest help {0} for more." });

				return;
			}

			switch (args.Parameters[0].ToLower())
			{
				case "claim":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to claim chests.");
						break;
					}
					args.Player.SendInfoMessage("Open a chest to claim it.");
					playerData[args.Player.Index].action = chestAction.protect;
					break;
				case "unclaim":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to claim chests.");
						break;
					}
					args.Player.SendInfoMessage("Open a chest to unclaim it.");
					playerData[args.Player.Index].action = chestAction.unProtect;
					break;
				case "info":
					if (!args.Player.HasPermission("ic.info"))
					{
						args.Player.SendErrorMessage("You do not have permission to view chest info.");
						break;
					}
					args.Player.SendInfoMessage("Open a chest to get information about it.");
					playerData[args.Player.Index].action = chestAction.info;
					break;
				case "search":
					if (!args.Player.HasPermission("ic.search"))
					{
						args.Player.SendErrorMessage("You do not have permission to search for chest items.");
						break;
					}
					if (args.Parameters.Count < 2)
					{
						args.Player.SendErrorMessage("Invalid syntax: /chest search <item name>");
						break;
					}
					string name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
					if (Main.itemName.ToList().Exists(p => p.ToLower() == name.ToLower()))
					{
						int itemid = Main.itemName.ToList().FindIndex(p => p.ToLower() == name.ToLower());
						int count = DB.searchChests(itemid);
						args.Player.SendSuccessMessage($"There are {count} chest(s) with {Main.itemName[itemid]}(s).");
					}
					else if (Main.itemName.ToList().Count(p => p.ToLower().Contains(name.ToLower())) == 1)
					{
						int itemid = Main.itemName.ToList().FindIndex(p => p.ToLower().Contains(name.ToLower()));
						int count = DB.searchChests(itemid);
						args.Player.SendSuccessMessage($"There are {count} chest(s) with {Main.itemName[itemid]}(s).");
					}
					else if (Main.itemName.ToList().Exists(p => p.ToLower().Contains(name.ToLower())))
					{
						args.Player.SendErrorMessage($"Multiple matches found for item '{name}'");
					}
					else
					{
						args.Player.SendErrorMessage($"No matches found for item '{name}'.");
					}
					break;
				case "allow":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to allow other users to access this chest.");
						return;
					}
					if (args.Parameters.Count < 2)
						args.Player.SendErrorMessage("Invalid syntax: /chest allow <player name>");
					else
					{
						name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
						var user = TShock.Users.GetUserByName(name);

						if (user == null)
						{
							args.Player.SendErrorMessage("No player found by the name " + name);
							return;
						}
						playerData[args.Player.Index].userIDToChange = user.ID;
						playerData[args.Player.Index].action = chestAction.allowUser;
						args.Player.SendInfoMessage("Open a chest to allow " + name + " to access it.");
					}
					break;
				case "remove":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to remove chest access from other users.");
						return;
					}
					if (args.Parameters.Count < 2)
						args.Player.SendErrorMessage("Invalid syntax: /chest remove <player name>");
					else
					{
						name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
						var user = TShock.Users.GetUserByName(name);

						if (user == null)
						{
							args.Player.SendErrorMessage("No player found by the name " + name);
							return;
						}
						playerData[args.Player.Index].userIDToChange = user.ID;
						playerData[args.Player.Index].action = chestAction.removeUser;
						args.Player.SendInfoMessage("Open a chest to remove chest access from  " + name + ".");
					}
					break;
				case "allowgroup":
				case "allowg":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to allow other groups to access this chest.");
						return;
					}
					if (args.Parameters.Count != 2)
						args.Player.SendErrorMessage("Invalid syntax: /chest allowgroup <group name>");
					else
					{
						var group = TShock.Groups.GetGroupByName(args.Parameters[1]);

						if (group == null)
						{
							args.Player.SendErrorMessage("No group found by the name " + args.Parameters[1]);
							return;
						}
						playerData[args.Player.Index].groupToChange = group.Name;
						playerData[args.Player.Index].action = chestAction.allowGroup;
						args.Player.SendInfoMessage("Open a chest to allow users from the group " + group.Name + " to access it.");
					}
					break;
				case "removegroup":
				case "removeg":
					if (!args.Player.HasPermission("ic.claim"))
					{
						args.Player.SendErrorMessage("You do not have permission to remove chest access from other groups.");
						return;
					}
					if (args.Parameters.Count != 2)
						args.Player.SendErrorMessage("Invalid syntax: /chest removegroup <group name>");
					else
					{
						var group = TShock.Groups.GetGroupByName(args.Parameters[1]);

						if (group == null)
						{
							args.Player.SendErrorMessage("No group found by the name " + args.Parameters[1]);
							return;
						}
						playerData[args.Player.Index].groupToChange = group.Name;
						playerData[args.Player.Index].action = chestAction.removeGroup;
						args.Player.SendInfoMessage("Open a chest to remove chest access from users in the group " + group.Name + ".");
					}
					break;
				case "public":
					if (!args.Player.HasPermission("ic.public"))
					{
						args.Player.SendErrorMessage("You do not have permission to change a chest's public setting.");
						break;
					}
					playerData[args.Player.Index].action = chestAction.togglePublic;
					args.Player.SendInfoMessage("Open a chest to toggle the chest's public setting.");
					break;
				case "refill":
					if (!args.Player.HasPermission("ic.refill"))
					{
						args.Player.SendErrorMessage("You do not have permission to set a chest's refill time.");
						break;
					}
					if (args.Parameters.Count != 2) // /chest refill <time>
					{
						args.Player.SendErrorMessage("Invalid syntax: /chest refill <seconds>");
						break;
					}
					int refillTime;
					if (!int.TryParse(args.Parameters[1], out refillTime) || refillTime < 0 || refillTime > 99999)
					{
						args.Player.SendErrorMessage("Invalid refill time.");
						break;
					}
					playerData[args.Player.Index].action = chestAction.setRefill;
					playerData[args.Player.Index].refillTime = refillTime;
					if (refillTime != 0)
						args.Player.SendInfoMessage("Open a chest to set its refill time to " + refillTime + " seconds.");
					else
						args.Player.SendInfoMessage("Open a chest to remove auto-refill.");
					break;
				case "save":
					if (!args.Player.HasPermission("ic.save"))
					{
						args.Player.SendErrorMessage("You do not have permission to save chests.");
						break;
					}
					if (args.Parameters.Count < 2)
					{
						args.Player.SendErrorMessage("Invalid syntax: /chest save <chest name>");
						break;
					}
					name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
					if (name.Length > 30)
					{
						args.Player.SendErrorMessage("Chest name too long!");
						break;
					}
					playerData[args.Player.Index].chestName = name;
					args.Player.SendInfoMessage("Open a chest to save it as '" + name + "'.");
					playerData[args.Player.Index].action = chestAction.setName;
					break;
				case "load":
					if (!args.Player.HasPermission("ic.save"))
					{
						args.Player.SendErrorMessage("You do not have permission to save chests.");
						break;
					}
					if (args.Parameters.Count < 2)
					{
						args.Player.SendErrorMessage("Invalid syntax: /chest load <chest name>");
						break;
					}
					name = string.Join(" ", args.Parameters.GetRange(1, args.Parameters.Count - 1));
					if (!DB.chestNameExists(name))
					{
						args.Player.SendErrorMessage("No chests found by the name '" + name + "'.");
						break;
					}
					playerData[args.Player.Index].chestName = name;
					args.Player.SendInfoMessage("Open a chest to load chest '" + name + "'.");
					args.Player.SendWarningMessage("Warning! The chest's current contents will be gone forever, even if it was previously saved!");
					playerData[args.Player.Index].action = chestAction.loadName;
					break;
				case "cancel":
					playerData[args.Player.Index].action = chestAction.none;
					args.Player.SendInfoMessage("Canceled chest action.");
					break;
				default:
					args.Player.SendErrorMessage("Invalid syntax. Use '/chest help' for help.");
					break;
			}
		}

		private async void ConvChests(CommandArgs args)
		{
			if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "-r")
			{
				if (DB.getChestCount() > 1000)
				{
					args.Player.SendErrorMessage("There are more than 1000 chests in the database, which is more than the map can hold.");
					return;
				}
				if (Main.player.ToList().Any(p => p.chest != -1))
				{
					args.Player.SendErrorMessage("This command cannot be ran while chests are in use.");
					return;
				}
				args.Player.SendWarningMessage("Restoring chests. Please wait...");
				await Task.Factory.StartNew(() => DB.restoreChests());
				args.Player.SendSuccessMessage("Restored chests. InfiniteChest features are now disabled.");
				return;
			}

			if (Main.player.ToList().Any(p => p.chest != -1))
			{
				args.Player.SendErrorMessage("This command cannot be ran while chests are in use.");
				return;
			}

			int converted = convertChests();
			args.Player.SendSuccessMessage($"Converted {converted} chest(s).");
			notInfChests = false;
		}

		private async void PruneChests(CommandArgs args)
		{
			args.Player.SendWarningMessage("Pruning empty chests. Please wait...");
			int results = await Task<int>.Factory.StartNew(() => DB.pruneChests(args.Player.Index));
			args.Player.SendSuccessMessage($"Destroyed {results} empty chests.");
		}
		#endregion

		private int convertChests()
		{
			int converted = 0;
			for (int i = 0; i < Main.chest.Length; i++)
			{
				Chest chest = Main.chest[i];
				if (chest != null && !Main.player.ToList().Exists(p => p.chest == i))
				{
					InfChest ichest = new InfChest()
					{
						items = chest.item,
						x = chest.x,
						y = chest.y,
						userid = -1
					};

					DB.addChest(ichest);
					converted++;
				}
				Main.chest[i] = null;
			}
			if (converted > 0)
			{
				TSPlayer.Server.SendInfoMessage("[InfChests] Converted " + converted + " chest(s).");
				WorldFile.saveWorld();
			}
			return converted;
		}
	}

	public enum chestAction
	{
		none,
		info,
		protect,
		unProtect,
		togglePublic,
		setRefill,
		setPassword,
		allowUser,
		removeUser,
		allowGroup,
		removeGroup,
		setName,
		loadName
	}
}
