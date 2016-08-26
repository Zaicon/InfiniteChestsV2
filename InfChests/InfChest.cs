using System.Collections.Generic;
using Terraria;

namespace InfChests
{
	public class InfChest
	{
		public int id;
		public int x;
		public int y;
		public int userid;
		public Item[] items;
		public bool isPublic;
		public int refillTime;
		public List<string> groups;
		public List<int> users;

		//public override string ToString()
		//{
		//	StringBuilder output = new StringBuilder();
		//	foreach (Item item in items)
		//	{
		//		output.Append(item.type);
		//		output.Append(",");
		//		output.Append(item.prefix);
		//		output.Append(",");
		//		output.Append(item.stack);
		//		output.Append("|");
		//	}
		//	string data = output.ToString();
		//	return data.TrimEnd('|');
		//}

		//public void setItemsFromString(string raw)
		//{
		//	List<Item> itemList = new List<Item>();
		//	string[] split = raw.Split('|');
		//	foreach(string str in split)
		//	{
		//		Item item = new Item();
		//		string[] part = str.Split(',');
		//		item.SetDefaults(int.Parse(part[0]));
		//		item.prefix = byte.Parse(part[1]);
		//		item.stack = int.Parse(part[2]);
		//		itemList.Add(item);
		//	}

		//	items = itemList.ToArray();
		//}
	}
}
