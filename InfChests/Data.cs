using System;

namespace InfChests
{
	public class Data
	{
		public int dbid;
		public int mainid;
		public string password;
		public chestAction action;
		public int refillTime;

		public Data()
		{
			dbid = -1;
			mainid = -1;
			password = string.Empty;
			action = chestAction.none;
			refillTime = 0;
		}
	}
}
