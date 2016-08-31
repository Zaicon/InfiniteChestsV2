using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;

namespace InfChests
{
	public static class DB
	{
		private static IDbConnection db;

		public static void Connect()
		{
			switch (TShock.Config.StorageType.ToLower())
			{
				case "mysql":
					string[] dbHost = TShock.Config.MySqlHost.Split(':');
					db = new MySqlConnection()
					{
						ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
							dbHost[0],
							dbHost.Length == 1 ? "3306" : dbHost[1],
							TShock.Config.MySqlDbName,
							TShock.Config.MySqlUsername,
							TShock.Config.MySqlPassword)

					};
					break;

				case "sqlite":
					string sql = Path.Combine(TShock.SavePath, "InfChests.sqlite");
					db = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
					break;

			}

			SqlTableCreator sqlcreator = new SqlTableCreator(db, db.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());

			sqlcreator.EnsureTableStructure(new SqlTable("InfChests",
				new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, Unique = true, Length = 7, AutoIncrement = true },
				new SqlColumn("UserID", MySqlDbType.Int32) { Length = 6 },
				new SqlColumn("X", MySqlDbType.Int32) { Length = 6 },
				new SqlColumn("Y", MySqlDbType.Int32) { Length = 6 },
				new SqlColumn("Name", MySqlDbType.Text) { Length = 30 },
				new SqlColumn("Public", MySqlDbType.Int32) { Length = 1 },
				new SqlColumn("Users", MySqlDbType.Text) { Length = 500 },
				new SqlColumn("Groups", MySqlDbType.Text) { Length = 500 },
				new SqlColumn("Refill", MySqlDbType.Int32) { Length = 5 },
				new SqlColumn("WorldID", MySqlDbType.Int32) { Length = 15 }));

			sqlcreator.EnsureTableStructure(new SqlTable("ChestItems",
				new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, Unique = true, Length = 10, AutoIncrement = true },
				new SqlColumn("Slot", MySqlDbType.Int32) { Length = 2 },
				new SqlColumn("Type", MySqlDbType.Int32) { Length = 4 },
				new SqlColumn("Stack", MySqlDbType.Int32) { Length = 3 },
				new SqlColumn("Prefix", MySqlDbType.Int32) { Length = 2 },
				new SqlColumn("WorldID", MySqlDbType.Int32) { Length = 15 },
				new SqlColumn("ChestID", MySqlDbType.Int32) { Length = 7 }));
		}

		public static bool addChest(InfChest _chest)
		{
			string query = $"SELECT * FROM InfChests WHERE X = {_chest.x} AND Y = {_chest.y} AND WorldID = {Main.worldID}";

			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
					return false;
			}

			query = $"INSERT INTO InfChests (UserID, X, Y, Name, Public, Refill, Users, Groups, WorldID) VALUES ({_chest.userid}, {_chest.x}, {_chest.y}, '', {0}, {0}, '', '', {Main.worldID})";
			int result = db.Query(query);
			query = $"SELECT MAX(ID) AS lastchestid FROM InfChests";

			int lastid = -1;

			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
				{
					try
					{
						lastid = reader.Get<int>("lastchestid");
					}
					catch
					{
						TSPlayer.Server.SendErrorMessage("Error getting last inserted row id.");
						return false;
					}
				}
			}
			for (int i = 0; i < _chest.items.Length; i++)
			{
				query = $"INSERT INTO ChestItems (Slot, Type, Stack, Prefix, WorldID, ChestID) VALUES ({i}, {_chest.items[i].type}, {_chest.items[i].stack}, {_chest.items[i].prefix}, {Main.worldID}, {lastid})";
				result += db.Query(query);
			}

			if (result == 41)
				return true;
			else
				return false;
		}

		public static bool removeChest(int id)
		{
			string query = $"DELETE FROM InfChests WHERE ID = {id}";
			int result = db.Query(query);
			query = $"DELETE FROM ChestItems WHERE ChestID = {id}";
			result += db.Query(query);
			if (result == 41)
				return true;
			else
				return false;
		}

		public static InfChest getChest(short tilex, short tiley)
		{
			string query = $"SELECT * FROM InfChests WHERE X = {tilex} AND Y = {tiley} AND WorldID = {Main.worldID}";
			InfChest chest = new InfChest();
			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
				{
					string _users = reader.Get<string>("Users");
					string _groups = reader.Get<string>("Groups");

					chest.id = reader.Get<int>("ID");
					chest.userid = reader.Get<int>("UserID");
					chest.x = tilex;
					chest.y = tiley;
					chest.isPublic = reader.Get<int>("Public") == 1 ? true : false;
					chest.refillTime = reader.Get<int>("Refill");
					chest.users = string.IsNullOrEmpty(_users) ? new List<int>() : _users.Split(',').ToList().ConvertAll<int>(p => int.Parse(p));
					chest.groups = string.IsNullOrEmpty(_groups) ? new List<string>() : _groups.Split(',').ToList();
					chest.items = new Item[40];
				}
				else
					return null;
			}
			query = $"SELECT * FROM ChestItems WHERE ChestID = {chest.id}";
			using (var reader = db.QueryReader(query))
			{
				while (reader.Read())
				{
					int slot = reader.Get<int>("Slot");
					int type = reader.Get<int>("Type");
					int stack = reader.Get<int>("Stack");
					int prefix = reader.Get<int>("Prefix");
					Item item = new Item();
					item.SetDefaults(type);
					item.stack = stack;
					item.prefix = (byte)prefix;
					chest.items[slot] = item;
				}
			}

			return chest;
		}

		public static InfChest getChest(string name)
		{
			string query = $"SELECT * FROM InfChests WHERE Name = '{name}'";
			InfChest chest = new InfChest();
			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
				{
					string _users = reader.Get<string>("Users");
					string _groups = reader.Get<string>("Groups");

					chest.id = reader.Get<int>("ID");
					chest.userid = reader.Get<int>("UserID");
					chest.x = reader.Get<int>("X");
					chest.y = reader.Get<int>("Y");
					chest.isPublic = reader.Get<int>("Public") == 1 ? true : false;
					chest.refillTime = reader.Get<int>("Refill");
					chest.users = string.IsNullOrEmpty(_users) ? new List<int>() : _users.Split(',').ToList().ConvertAll<int>(p => int.Parse(p));
					chest.groups = string.IsNullOrEmpty(_groups) ? new List<string>() : _groups.Split(',').ToList();
					chest.items = new Item[40];
				}
				else
					return null;
			}
			query = $"SELECT * FROM ChestItems WHERE ChestID = {chest.id}";
			using (var reader = db.QueryReader(query))
			{
				while (reader.Read())
				{
					int slot = reader.Get<int>("Slot");
					int type = reader.Get<int>("Type");
					int stack = reader.Get<int>("Stack");
					int prefix = reader.Get<int>("Prefix");
					Item item = new Item();
					item.SetDefaults(type);
					item.stack = stack;
					item.prefix = (byte)prefix;
					chest.items[slot] = item;
				}
			}

			return chest;
		}

		public static bool setUserID(int id, int userid)
		{
			string query = $"UPDATE InfChests SET UserID = {userid} WHERE ID = {id} AND WorldID = {Main.worldID}";
			int result = db.Query(query);
			if (result != 1)
				return false;
			else
				return true;
		}
		
		public static bool setName(int id, string name)
		{
			if (chestNameExists(name))
				return false;

			string query = $"UPDATE InfChests SET Name = '{name}' WHERE ID = {id}";
			int result = db.Query(query);
			if (result == 1)
				return true;
			else
				return false;
		}

		public static bool setItem(int id, Item item, int slot)
		{
			if (item == null)
			{
				TShock.Log.ConsoleError("Error setting items (nullref).");
				return false;
			}
			
			string query = $"UPDATE ChestItems SET Type = {item.type}, Stack = {item.stack}, Prefix = {item.prefix} WHERE ChestID = {id} AND Slot = {slot}";

			int result = db.Query(query);

			if (result == 1)
				return true;
			else
				return false;
		}

		public static bool setPublic(int id, bool isPublic)
		{
			int num = isPublic ? 1 : 0;
			string query = $"UPDATE InfChests SET Public = {num} WHERE ID = {id} AND WorldID = {Main.worldID}";
			int result = db.Query(query);
			if (result != 1)
				return false;
			else
				return true;
		}

		public static bool setGroups(int id, List<string> groups)
		{
			string query = $"UPDATE InfChests SET Groups = '{string.Join(",", groups)}' WHERE ID = {id} AND WorldID = {Main.worldID}";
			int result = db.Query(query);
			if (result != 1)
				return false;
			else
				return true;
		}

		public static bool setUsers(int id, List<int> users)
		{
			string query = $"UPDATE InfChests SET Users = '{string.Join(",", users.Select(p => p.ToString()))}' WHERE ID = {id} AND WorldID = {Main.worldID}";
			int result = db.Query(query);
			if (result != 1)
				return false;
			else
				return true;
		}

		public static bool setRefill(int id, int seconds)
		{
			string query = $"UPDATE InfChests SET Refill = {seconds} WHERE ID = {id} AND WorldID = {Main.worldID}";
			int result = db.Query(query);
			if (result != 1)
				return false;
			else
				return true;
		}

		public static bool setAll(int id, InfChest chest)
		{
			int ispublic = chest.isPublic ? 1 : 0;
			int result = 0;

			string query = $"UPDATE InfChests SET UserID = {chest.userid}, Public = {ispublic}, Users = '{string.Join(",", chest.users.Select(p => p.ToString()))}', Groups = '{string.Join(",", chest.groups)}', Refill = {chest.refillTime} WHERE ID = {id}";
			result += db.Query(query);
			for (int i = 0; i < chest.items.Length; i++)
			{
				query = $"UPDATE ChestItems SET Type = {chest.items[i].type}, Stack = {chest.items[i].stack}, Prefix = {chest.items[i].prefix} WHERE ChestID = {id} AND Slot = {i}";
				result += db.Query(query);
			}

			if (result == 41)
				return true;
			else
				return false;
		}

		public static int getChestCount()
		{
			string query = $"SELECT COUNT(*) AS RowCount FROM InfChests WHERE WorldID = {Main.worldID}";
			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
					return reader.Get<int>("RowCount");
			}
			throw new Exception("Database error.");
		}

		public static void restoreChests()
		{
			List<InfChest> chestList = new List<InfChest>();

			InfChests.lockChests = true;
			string query = $"SELECT * FROM InfChests WHERE WorldID = {Main.worldID}";
			using (var reader = db.QueryReader(query))
			{ 
				while (reader.Read())
				{
					InfChest chest = new InfChest();
					chest.id = reader.Get<int>("ID");
					chest.x = reader.Get<int>("X");
					chest.y = reader.Get<int>("Y");
					chest.items = new Item[40];
					chestList.Add(chest);
				}
			}
			foreach (InfChest chest in chestList)
			{
				query = $"SELECT * FROM ChestItems WHERE ChestID = {chest.id}";
				using (var reader = db.QueryReader(query))
				{
					while (reader.Read())
					{
						int slot = reader.Get<int>("Slot");
						int type = reader.Get<int>("Type");
						int stack = reader.Get<int>("Stack");
						int prefix = reader.Get<int>("Prefix");
						Item item = new Item();
						item.SetDefaults(type);
						item.stack = stack;
						item.prefix = (byte)prefix;
						chest.items[slot] = item;
					}
				}
			}
			for (int i = 0; i < chestList.Count; i++)
			{
				Main.chest[i] = new Chest();
				Main.chest[i].x = chestList[i].x;
				Main.chest[i].y = chestList[i].y;
				Main.chest[i].item = chestList[i].items;
			}
			query = $"DELETE FROM InfChests WHERE WorldID = {Main.worldID}";
			db.Query(query);
			query = $"DELETE FROM ChestItems WHERE WorldID = {Main.worldID}";
			db.Query(query);
			TShock.Utils.SaveWorld();
			InfChests.lockChests = false;
			InfChests.notInfChests = true;
		}

		public static int pruneChests(int index)
		{
			string query = $"SELECT * FROM ChestItems WHERE Type = 0 AND WorldID = {Main.worldID}";
			List<int> chestIDs = new List<int>();
			using (var reader = db.QueryReader(query))
			{
				while (reader.Read())
				{
					chestIDs.Add(reader.Get<int>("ChestID"));
				}
			}

			query = $"SELECT * FROM InfChests WHERE ID IN ({string.Join(",", chestIDs)})";
			using (var reader = db.QueryReader(query))
			{
				while (reader.Read())
				{
					int tilex = reader.Get<int>("X");
					int tiley = reader.Get<int>("Y");
					WorldGen.KillTile(tilex, tiley, noItem: true);
					foreach (TSPlayer plr in TShock.Players.Where(p => p != null && p.Active))
						plr.SendTileSquare(tilex, tiley, 3);
				}
			}

			query = $"DELETE FROM InfChests WHERE ID IN ({string.Join(",", chestIDs)})";
			int count = db.Query(query);
			query = $"DELETE FROM ChestItems WHERE Type = 0 AND WorldID = {Main.worldID}";
			db.Query(query);
			return count;
		}

		public static List<InfChest> getNearbyChests(Point point)
		{
			int x1 = point.X - 25;
			int x2 = point.X + 25;
			int y1 = point.Y - 8;
			int y2 = point.Y + 8;

			List<InfChest> chests = new List<InfChest>();

			string query = $"SELECT * FROM InfChests WHERE WorldID = {Main.worldID} AND X > {x1} AND X < {x2} AND Y > {y1} AND Y < {y2}";
			using (var reader = db.QueryReader(query))
			{
				while (reader.Read())
				{
					chests.Add(new InfChest() {
						id = reader.Get<int>("ID"),
						refillTime = reader.Get<int>("Refill"),
						userid = reader.Get<int>("UserID")
					});
					//Not filling other stuff since it's not used in NearbyChests
				}
			}

			foreach (InfChest chest in chests)
			{
				int id = chest.id;
				query = $"SELECT * FROM ChestItems WHERE ChestID = {id}";
				using (var reader = db.QueryReader(query))
				{
					while (reader.Read())
					{
						int slot = reader.Get<int>("Slot");
						int type = reader.Get<int>("Type");
						int stack = reader.Get<int>("Stack");
						int prefix = reader.Get<int>("Prefix");
						Item item = new Item();
						item.SetDefaults(type);
						item.stack = stack;
						item.prefix = (byte)prefix;
						chest.items[slot] = item;
					}
				}
			}

			return chests;
		}

		public static int searchChests(int itemid)
		{
			string query = $"SELECT Count(DISTINCT ChestID) AS itemcount FROM ChestItems WHERE Type = {itemid} AND WorldID = {Main.worldID}";
			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
				{
					return reader.Get<int>("itemcount");
				}
				else
				{
					TShock.Log.ConsoleError("Error getting itemcount from DB.");
					return -1;
				}
			}
		}

		public static bool chestNameExists(string name)
		{
			string query = $"SELECT COUNT(*) AS chestcount FROM InfChests WHERE Name = '{name}'";
			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
				{
					int count = reader.Get<int>("chestcount");
					if (count != 0)
						return true;
					else
						return false;
				}
				else
					return false;
			}
		}

		public static bool isRefill(int chestid)
		{
			string query = $"SELECT Refill FROM InfChests WHERE ID = {chestid}";
			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
				{
					int refill = reader.Get<int>("Refill");
					if (refill > 0)
						return true;
					else
						return false;
				}
				else
					return false;
			}
		}
	}
}
