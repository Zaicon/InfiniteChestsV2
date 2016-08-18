using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using System;
using System.Data;
using System.IO;
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
				new SqlColumn("Refill", MySqlDbType.Int32) { Length = 5 },
				new SqlColumn("WorldID", MySqlDbType.Int32) { Length = 15 }));
		}

		public static bool addChest(InfChest _chest)
		{
			string query = $"INSERT INTO InfChests (UserID, X, Y, Items, Public, Refill, WorldID) VALUES ({_chest.userid}, {_chest.x}, {_chest.y}, '{_chest.ToString()}', {0}, {0}, {Main.worldID})";
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
					InfChest chest = new InfChest()
					{
						id = reader.Get<int>("ID"),
						userid = reader.Get<int>("UserID"),
						x = tilex,
						y = tiley,
						password = reader.Get<string>("Password"),
						isPublic = reader.Get<int>("Public") == 1 ? true : false,
						refillTime = reader.Get<int>("Refill")
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
	}
}
