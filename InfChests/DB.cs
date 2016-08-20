using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Terraria;
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
				new SqlColumn("Items", MySqlDbType.Text) { Length = 500 },
				new SqlColumn("Password", MySqlDbType.Text) { Length = 100 },
				new SqlColumn("Public", MySqlDbType.Int32) { Length = 1 },
				new SqlColumn("Users", MySqlDbType.Text) { Length = 500 },
				new SqlColumn("Groups", MySqlDbType.Text) { Length = 500 },
				new SqlColumn("Refill", MySqlDbType.Int32) { Length = 5 },
				new SqlColumn("WorldID", MySqlDbType.Int32) { Length = 15 }));

			//sqlcreator.EnsureTableStructure(new SqlTable("ChestItems",
			//	new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, Unique = true, Length = 10, AutoIncrement = true },
			//	new SqlColumn("Slot", MySqlDbType.Int32) { Length = 2 },
			//	new SqlColumn("Type", MySqlDbType.Int32) { Length = 4 },
			//	new SqlColumn("Stack", MySqlDbType.Int32) { Length = 3 },
			//	new SqlColumn("Prefix", MySqlDbType.Int32) { Length = 2 },
			//	new SqlColumn("ChestID", MySqlDbType.Int32) { Length = 7 }));

			//db.Execute("ALTER TABLE ChestItems ADD FOREIGN KEY (ChestID) REFERENCES InfChests(ID);");
		}

		//public static bool addChest(InfChest _chest)
		//{
		//	string query = $"INSERT INTO InfChests (UserID, X, Y, Items, Public, Refill, WorldID) VALUES ({_chest.userid}, {_chest.x}, {_chest.y}, '{_chest.ToString()}', {0}, {0}, {Main.worldID})";
		//string query = "INSERT INTO InfChests (UserID, X, Y, Public, Refill, WorldID) VALUES (@userid, @x, @y, @public, @refill, @worldid);";
		//int result = db.Execute(query, new { userid = _chest.userid, x = _chest.x, y = _chest.y, @public = _chest.isPublic, refill = _chest.refillTime, worldid = Main.worldID});
		//	int result = db.Query(query);
		//	if (result != 1)
		//	{
		//		TShock.Log.ConsoleError("Unable to add Chest to database.");
		//		return false;
		//	}
		//foreach (InfItem item in _chest.items)
		//{
		//	query = "INSERT INTO "
		//}
		//}

		public static bool addChest(InfChest _chest)
		{
			string query = $"INSERT INTO InfChests (UserID, X, Y, Items, Public, Refill, Users, Groups, WorldID) VALUES ({_chest.userid}, {_chest.x}, {_chest.y}, '{_chest.ToString()}', {0}, {0}, '', '', {Main.worldID})";
			int result = db.Query(query);
			if (result == 1)
				return true;
			else
				return false;
		}

		public static bool removeChest(int id)
		{
			string query = $"DELETE FROM InfChests WHERE ID = {id}";
			int result = db.Query(query);
			if (result == 1)
				return true;
			else
				return false;
		}

		public static InfChest getChest(short tilex, short tiley)
		{
			string query = $"SELECT * FROM InfChests WHERE X = {tilex} AND Y = {tiley} AND WorldID = {Main.worldID}";
			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
				{
					string _users = reader.Get<string>("Users");
					string _groups = reader.Get<string>("Groups");
					
					InfChest chest = new InfChest()
					{
						id = reader.Get<int>("ID"),
						userid = reader.Get<int>("UserID"),
						x = tilex,
						y = tiley,
						password = reader.Get<string>("Password"),
						isPublic = reader.Get<int>("Public") == 1 ? true : false,
						refillTime = reader.Get<int>("Refill"),
						users = string.IsNullOrEmpty(_users) ? new List<int>() : _users.Split(',').ToList().ConvertAll<int>(p => int.Parse(p)),
						groups = string.IsNullOrEmpty(_groups) ? new List<string>() : _groups.Split(',').ToList()
					};
					chest.setItemsFromString(reader.Get<string>("Items"));

					return chest;
				}
			}

			return null;
		}

		public static bool setPassword(int id, string password)
		{
			string query = $"UPDATE InfChests SET Password = '{password}' WHERE ID = {id} AND WorldID = {Main.worldID}";
			int result = db.Query(query);
			if (result != 1)
				return false;
			else
				return true;
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

		public static bool setItems(int id, Item[] items)
		{
			InfChest chest = new InfChest();
			chest.items = items;
			string query = $"UPDATE InfChests SET Items = '{chest.ToString()}' WHERE ID = {id} AND WorldID = {Main.worldID}";
			int result = db.Query(query);
			if (result != 1)
				return false;
			else
				return true;
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

		public static int getChestCount()
		{
			string query = "SELECT COUNT(*) AS RowCount FROM InfChests";
			using (var reader = db.QueryReader(query))
			{
				if (reader.Read())
					return reader.Get<int>("RowCount");
			}
			throw new Exception("Database error.");
		}

		public static void restoreChests()
		{
			InfChests.lockChests = true;
			string query = $"SELECT * FROM InfChests WHERE WorldID = {Main.worldID}";
			using (var reader = db.QueryReader(query))
			{
				int count = 0;
				while (reader.Read())
				{
					InfChest temp = new InfChest();
					temp.setItemsFromString(reader.Get<string>("Items"));
					Main.chest[count] = new Chest()
					{
						item = temp.items,
						x = reader.Get<int>("X"),
						y = reader.Get<int>("Y")
					};
					count++;
				}
			}
			query = $"DELETE FROM InfChests WHERE WorldID = {Main.worldID}";
			db.Query(query);
			TShock.Utils.SaveWorld();
			InfChests.lockChests = false;
			InfChests.notInfChests = true;
		}

		public static int pruneChests(int index)
		{
			string blank = "0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0|0,0,0";

			string query = $"SELECT * FROM InfChests WHERE Items = '{blank}' AND WorldID = {Main.worldID}";
			using (var reader = db.QueryReader(query))
			{
				while (reader.Read())
				{
					int tilex = reader.Get<int>("X");
					int tiley = reader.Get<int>("Y");
					WorldGen.KillTile(tilex, tiley, noItem: true);
					NetMessage.SendTileSquare(index, tilex, tiley, 3);
				}
			}

			query = $"DELETE FROM InfChests WHERE Items = '{blank}' AND WorldID = {Main.worldID}";
			int count = db.Query(query);
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
						password = reader.Get<string>("Password"),
						refillTime = reader.Get<int>("Refill"),
						userid = reader.Get<int>("UserID")
					});
					chests[chests.Count - 1].setItemsFromString(reader.Get<string>("Items"));
					//Not filling other stuff since it's not used in NearbyChests
				}
			}

			return chests;
		}
	}
}
