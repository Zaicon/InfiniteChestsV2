using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Terraria;
using TShockAPI;

namespace InfChests
{
	public class Data
	{
		public int dbid;
		public int mainid;
		public chestAction action;
		public int refillTime;
		public Timer quickStackTimer;
		public List<int> slotQueue;
		public Point location;
		private int index;
		public int userIDToChange;
		public string groupToChange;
		public bool lockChests;
		public string chestName;
		public int transactionsLeft;
		public bool hasClosed;

		public Data(int index)
		{
			dbid = -1;
			mainid = -1;
			action = chestAction.none;
			refillTime = 0;
			quickStackTimer = new Timer(1000); //wait one second for all quickstack packets to arrive
			quickStackTimer.Elapsed += onElapsed;
			slotQueue = new List<int>();
			this.index = index;
			userIDToChange = -1;
			groupToChange = "";
			lockChests = false;
			chestName = "";
			transactionsLeft = 0;
			hasClosed = false;
		}

		public void onElapsed(object sender, ElapsedEventArgs args)
		{
			List<InfChest> nearbyChests = DB.getNearbyChests(location);
			List<Item[]> original = new List<Item[]>();
			foreach(InfChest chest in nearbyChests)
			{
				original.Add((Item[])chest.items.Clone());
			}
			TSPlayer player = TShock.Players[index];
			Dictionary<int, Item> slotInfo = new Dictionary<int, Item>();
			slotQueue.Sort();
			foreach(int slot in slotQueue)
			{
				slotInfo.Add(slot, new Item());
				slotInfo[slot] = player.TPlayer.inventory[slot];
			}

			//foreach item in player's inventory
			for (int l = 0; l < slotInfo.Count; l++)
			{
				Item item = slotInfo.ElementAt(l).Value;
				//foreach chest nearby
				for (int i = 0; i < nearbyChests.Count; i++)
				{
					bool emptySlots = false;
					bool stacking = false;
					//if player has access to chest
					if ((nearbyChests[i].userid == player.User.ID || nearbyChests[i].userid == -1 || nearbyChests[i].isPublic || player.HasPermission("ic.edit")) && !InfChests.playerData.Values.Any(p => p.dbid == nearbyChests[i].id) && nearbyChests[i].refillTime == 0)
					{
						//foreach slot in chest
						for (int j = 0; j < nearbyChests[i].items.Length; j++)
						{
							//if slot is empty
							if (nearbyChests[i].items[j].type <= 0 || nearbyChests[i].items[j].stack <= 0)
							{
								emptySlots = true;
							}
							//else if slot has equivalent item
							else if (item.IsTheSameAs(nearbyChests[i].items[j]))
							{
								//stacks items into the chest
								stacking = true;
								int num = nearbyChests[i].items[j].maxStack - nearbyChests[i].items[j].stack;
								if (num > 0)
								{
									if (num > item.stack)
									{
										num = item.stack;
									}
									item.stack -= num;
									nearbyChests[i].items[j].stack += num;
									if (item.stack <= 0)
									{
										item.SetDefaults(0, false);
									}
								}
							}
						}
						//if item was partially stacked into a slot, and chest still has open slots remaining
						if (stacking && emptySlots && item.stack > 0)
						{
							//places items into empty slots in the chest
							for (int k = 0; k < nearbyChests[i].items.Length; k++)
							{
								if (nearbyChests[i].items[k].type == 0 || nearbyChests[i].items[k].stack == 0)
								{
									nearbyChests[i].items[k] = item.Clone();
									item.SetDefaults(0, false);
								}
							}
						}
					}
				}
			}

			//update players
			foreach(KeyValuePair<int, Item> kvp in slotInfo)
			{
				NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", index, kvp.Key, kvp.Value.prefix, kvp.Value.stack, kvp.Value.type);
			}
			//update database
			for (int i = 0; i < nearbyChests.Count; i++)
			{
				for (int j = 0; j < 50; j++)
				{
					if (nearbyChests[i].items[j] == original[i][j])
						continue;
					else
						DB.setItem(nearbyChests[i].id, nearbyChests[i].items[j], j);
				}
			}
			//restore defaults
			slotQueue.Clear();
			location = new Point();
		}
	}
}
