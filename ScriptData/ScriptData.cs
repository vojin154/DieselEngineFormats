namespace DieselEngineFormats.ScriptData
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Globalization;

	public class ScriptData
	{
		#region Fields

		public dynamic Root;
		private readonly BinaryReader br;
		private readonly FileStream fs_copy;
		private readonly Stack<long> savedPositions = new Stack<long>();
		private int floatOffset;
		private int idstringOffset;
		private string path;
		private int quaternionOffset;
		private int stringOffset;
		private int tableOffset;
		private int vectorOffset;
		private bool x64;

		public bool Raid { get; set; }
		//Exports
		private int table_count = 1;
		private MemoryStream table_index = new MemoryStream();
		private Dictionary<long, long> table_offsets_fix = new Dictionary<long, long>();
		private List<float> floats = new List<float>();
		private List<string> strings = new List<string>();
		private List<float[]> vectors = new List<float[]>();
		private List<float[]> quaternions = new List<float[]>();
		private List<ulong> idstrings = new List<ulong>();
		private List<Dictionary<string, object>> tables = new List<Dictionary<string, object>>();


		#endregion

		#region Constructors and Destructors

		public ScriptData() { }

		public ScriptData(string path)
		{
			this.path =  path;
			using (this.fs_copy = new FileStream(path, FileMode.Open, FileAccess.Read))
			{
				br = new BinaryReader(fs_copy);
				ReadHeader();
				Root = ParseItem();
				br.Close();
			}
		}

		public ScriptData(BinaryReader bReader, bool raid=false)
		{
			Raid = raid;
			br = bReader;
			ReadHeader();
			Root = ParseItem();
			br.Close();
		}

		#endregion

		#region Methods
		private void ReadHeader()
		{
			uint tag = br.ReadUInt32();

			//x64 for now is only PD2 Linux.
			//Raid doesn't have this tag :/
			if (Raid || tag == 568494624)
				x64 = true;

			Seek(0);

			//Raid / x64(Linux) / x32
			floatOffset = ReadOffset();     //24/16/12
			stringOffset = ReadOffset();    //56/40/28
			vectorOffset = ReadOffset();    //88/64/44
			quaternionOffset = ReadOffset();//120/88/60
			idstringOffset = ReadOffset();  //152/112/76
			tableOffset = ReadOffset();     //184/136/92

			if (x64)
				SeekAdvance(8); //Raid: 200 (Raid) / 152 (x64)
			else
				SeekAdvance(4); //100
		}

		private int ReadOffset(bool advance = true)
		{
			if (advance)
			{
				if (Raid)
					SeekAdvance(24);
				else if (x64)
					SeekAdvance(16);
				else
					SeekAdvance(12);
			}

			if (x64)
				return (int)br.ReadInt64();
			else
				return br.ReadInt32();
		}

		private int ReadIntValue()
		{
			if (Raid)
				return (int)br.ReadInt64();
			else
				return br.ReadInt32();
		}

		private dynamic ParseItem()
		{
			uint item_type = br.ReadUInt32();
			int value = (int)item_type & 0xFFFFFF;
			item_type = (item_type >> 24) & 0xFF;

			return item_type switch
			{
				//Nil
				0 => null,
				//False
				1 => false,
				//True
				2 => true,
				//Number
				3 => ReadFloat(value),
				//String
				4 => ReadString(value),
				//Vector
				5 => ReadVector(value),
				//Quaternion
				6 => ReadQuaternion(value),
				//IdString
				7 => ReadIdString(value),
				//Table
				8 => ReadTable(value),
				_ => null,
			};
		}

		private float ReadFloat(int index)
		{
			float return_float;
			SeekPush();
			Seek(floatOffset + index * 4);
			return_float = br.ReadSingle();
			SeekPop();
			return return_float;
		}

		private ulong ReadIdString(int index)
		{
			ulong return_idstring;
			SeekPush();
			Seek(idstringOffset + index * 8);
			return_idstring = br.ReadUInt64();
			SeekPop();
			return return_idstring;
		}

		private float[] ReadQuaternion(int index)
		{
			
			var return_quaternion = new float[4];
			SeekPush();
			Seek(quaternionOffset + index * 16);
			return_quaternion[0] = br.ReadSingle();
			return_quaternion[1] = br.ReadSingle();
			return_quaternion[2] = br.ReadSingle();
			return_quaternion[3] = br.ReadSingle();
			SeekPop();
			return return_quaternion;
		}

		private string ReadString(int index)
		{
			string return_string = "";
			SeekPush();

			Seek(stringOffset + (index * (x64 ? 16 : 8)) + (x64 ? 8 : 4));
			int real_offset = ReadOffset(false);

			Seek(real_offset);
			var inchar = (char)br.ReadByte();
			while (inchar != '\0')
			{
				return_string += inchar;
				inchar = (char)br.ReadByte();
			}
			SeekPop();
			//if (return_string.Contains("metadata"))
			 //   Console.WriteLine();
			return return_string;
		}

		private Dictionary<string, object> ReadTable(int index)
		{
			var return_table = new Dictionary<string, object>();
			SeekPush ();

			Seek(tableOffset + (index * (x64 ? (Raid ? 40 : 32) : 20)));

			int metatable_offset = ReadOffset(false);

			int item_count = ReadIntValue();
			ReadIntValue(); // Unknown : count
			int items_offset = ReadOffset(false);

			if (metatable_offset >= 0)
				return_table["_meta"] = ReadString(metatable_offset);
			for (int current_item = 0; current_item < item_count; ++current_item)
			{
				Seek(items_offset + (current_item * 8));
				dynamic key_item = ParseItem().ToString();
				dynamic value_item = ParseItem();
				return_table[key_item] = value_item;
			}
			SeekPop();
			return return_table;
		}

		private float[] ReadVector(int index)
		{
			var return_vector = new float[3];
			SeekPush();
			Seek(vectorOffset + index * 12);
			return_vector[0] = br.ReadSingle();
			return_vector[1] = br.ReadSingle();
			return_vector[2] = br.ReadSingle();
			SeekPop();
			return return_vector;
		}

		private void Seek(int offset)
		{
			br.BaseStream.Seek(offset, SeekOrigin.Begin);
		}

		private void SeekAdvance(int offset)
		{
			br.BaseStream.Seek(offset, SeekOrigin.Current);
		}

		private void SeekPop()
		{
			br.BaseStream.Seek(savedPositions.Pop(), SeekOrigin.Begin);
			//if (savedPositions.Count == 0)
			 //   Console.WriteLine();
		}

		private void SeekPush()
		{
			savedPositions.Push(br.BaseStream.Position);
		}

		private void WriteTable(ref Dictionary<string, object> data, List<string> path, ref BinaryWriter bw)
		{
			Dictionary<string, object> currentData = data;
			if (path.Count > 0)
			{
				foreach(string p in path)
					currentData = (Dictionary<string, object>)(currentData[p]);
			}

			int meta = -1;
			if (currentData.ContainsKey("_meta"))
				meta = this.strings.LastIndexOf((string)currentData["_meta"]);

			bw.Write(meta);
			bw.Write(currentData.Count - (meta > -1 ? 1 : 0));
			bw.Write(currentData.Count - (meta > -1 ? 1 : 0));
			this.table_offsets_fix.Add(bw.BaseStream.Position, this.table_index.Length);
			bw.Write((uint)(this.table_index.Length ));
			bw.Write(2037412641);

			BinaryWriter bwindex = new BinaryWriter(this.table_index);
			bwindex.Seek(0, SeekOrigin.End);
			long bwi_pos = bwindex.BaseStream.Position;
			byte[] buffer = new byte[currentData.Count*8];
			bwindex.Write(buffer);
			bwindex.BaseStream.Position = bwi_pos;

			Dictionary<String, Dictionary<string, object>> dicts_to_work = new Dictionary<String, Dictionary<string, object>>();

			foreach (KeyValuePair<string, object> keypair in currentData)
			{
				if (!keypair.Key.Equals("_meta"))
				{
					//key
					float floatparse;
					ulong ulongparse;
					if (float.TryParse(keypair.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out floatparse))
					{
						bwindex.Write((ushort)this.floats.LastIndexOf(floatparse));
						bwindex.Write((byte)0);
						bwindex.Write((byte)getItemType(floatparse));
					}
					else if (ulong.TryParse(keypair.Key, out ulongparse))
					{
						bwindex.Write((ushort)this.idstrings.LastIndexOf(ulongparse));
						bwindex.Write((byte)0);
						bwindex.Write((byte)getItemType(ulongparse));
					}
					else
					{
						bwindex.Write((ushort)this.strings.LastIndexOf(keypair.Key));
						bwindex.Write((byte)0);
						bwindex.Write((byte)getItemType(keypair.Key));
					}

					ulong tryp;
					//value
					if (keypair.Value is Dictionary<string, object>)
					{
						int data_index = this.table_count++;
						bwindex.Write((ushort)(data_index));

						long save_pos = bwindex.BaseStream.Position;

						List<string> currPath = new List<string>(path);
						currPath.Add(keypair.Key);
						WriteTable(ref data, currPath, ref bw);

						bwindex.BaseStream.Position = save_pos;
					}
					else if (keypair.Value is double)
					{
						bwindex.Write((ushort)this.floats.LastIndexOf(Convert.ToSingle((double)keypair.Value)));
					}
					else if (keypair.Value is float)
					{
						bwindex.Write((ushort)this.floats.LastIndexOf(Convert.ToSingle(keypair.Value)));
					}
					else if (keypair.Value is string)
					{
						if (this.strings.Contains(keypair.Value as string))
							bwindex.Write((ushort)this.strings.LastIndexOf((string)keypair.Value));
						else
						{
							float floattest;
							if (float.TryParse((string)keypair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out floattest))
							{
								bwindex.Write((ushort)this.floats.LastIndexOf(floattest));
							}
						}
					}
					else if (keypair.Value is Single[])
					{
						float[] temp = (float[])keypair.Value;
						if (temp.Length == 3)
							bwindex.Write((ushort)listVectorLastIndex(ref this.vectors, ref temp));
						else if (temp.Length == 4)
							bwindex.Write((ushort)listQuaternionLastIndex(ref this.quaternions, ref temp));
					}
					else if (ulong.TryParse(keypair.Value.ToString(), out tryp))
					{
						bwindex.Write((ushort)this.idstrings.LastIndexOf(tryp));
					}
					else if (keypair.Value is bool)
					{
						bwindex.Write((ushort)(Convert.ToInt32((bool)keypair.Value) + 1));
					}
					else
					{
						bwindex.Write((ushort)(0));
					}

					bwindex.Write((byte)0);
					bwindex.Write((byte)getItemType(keypair.Value));
				}
			}

			foreach (KeyValuePair<string, Dictionary<string, object>> keypair in dicts_to_work)
			{
				List<string> currPath = new List<string>(path);
				currPath.Add(keypair.Key);
				WriteTable(ref data, currPath, ref bw);
			}
		}

		public void export(Dictionary<string, object> data, String filename)
		{
			this.floats = new List<float>();
			this.strings = new List<string>();
			this.vectors = new List<float[]>();
			this.quaternions = new List<float[]>();
			this.idstrings = new List<ulong>();
			this.tables = new List<Dictionary<string, object>>();

			organize(ref data, ref this.floats, ref this.strings, ref this.vectors, ref this.quaternions, ref this.idstrings, ref this.tables);

			this.strings.Remove("_meta");

			using (FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite))
			{
				BinaryWriter bw = new BinaryWriter(fs);

				uint floatOffset = 0;
				uint stringOffset = 0;
				uint vectorOffset = 0;
				uint quaternionOffset = 0;
				uint idstringOffset = 0;
				uint tableOffset = 0;

				//Header
				bw.Write(2037412641);
				bw.Write(this.floats.Count);
				bw.Write(this.floats.Count);
				bw.Write(floatOffset);

				bw.Write(2037412641);
				bw.Write(this.strings.Count);
				bw.Write(this.strings.Count);
				bw.Write(stringOffset);

				bw.Write(2037412641);
				bw.Write(this.vectors.Count);
				bw.Write(this.vectors.Count);
				bw.Write(vectorOffset);

				bw.Write(2037412641);
				bw.Write(this.quaternions.Count);
				bw.Write(this.quaternions.Count);
				bw.Write(quaternionOffset);

				bw.Write(2037412641);
				bw.Write(this.idstrings.Count);
				bw.Write(this.idstrings.Count);
				bw.Write(idstringOffset);

				bw.Write(2037412641);
				bw.Write(this.tables.Count+1);
				bw.Write(this.tables.Count+1);
				bw.Write(tableOffset);

				//Body

				bw.Write(2037412641);
				bw.Write((ushort)0);
				bw.Write((byte)0);
				bw.Write((byte)8);

				//Write floats
				if (this.floats.Count > 0)
				{
					floatOffset = (uint)fs.Position;
					foreach (float number in this.floats)
						bw.Write(number);
				}

				//Write strings
				if (this.strings.Count > 0)
				{
					stringOffset = (uint)fs.Position;
					int soffset = this.strings.Count * 8;
					int accumulatedstringlegth = 0;

					foreach (string text in this.strings)
					{
						bw.Write(2037412641);
						bw.Write((uint)(stringOffset + soffset + accumulatedstringlegth));

						accumulatedstringlegth += text.Length + 1;

					}

					foreach (string text in this.strings)
					{
						foreach (char c in text)
						{
							bw.Write(c);
						}
						bw.Write((byte)0);
					}

					for (int x = 0; x < (fs.Position % 4); x++)
						bw.Write((byte)0);
				}

				//Write vectors
				if (this.vectors.Count > 0)
				{
					vectorOffset = (uint)fs.Position;
					foreach (float[] vector in this.vectors)
					{
						bw.Write(vector[0]);
						bw.Write(vector[1]);
						bw.Write(vector[2]);
					}
				}

				//Write quaternions
				if (this.quaternions.Count > 0)
				{
					quaternionOffset = (uint)fs.Position;
					foreach (float[] quaternion in this.quaternions)
					{
						bw.Write(quaternion[0]);
						bw.Write(quaternion[1]);
						bw.Write(quaternion[2]);
						bw.Write(quaternion[3]);
					}
				}

				//Write idstrings
				if (this.idstrings.Count > 0)
				{
					idstringOffset = (uint)fs.Position;
					foreach (ulong idstring in this.idstrings)
						bw.Write(idstring);
				}

				//Write tables
				if (this.tables.Count > 0)
				{
					tableOffset = (uint)fs.Position;

					List<string> path = new List<string>();
					WriteTable(ref data, path, ref bw);

					long item_offset = bw.BaseStream.Position;
					foreach(KeyValuePair<long, long> pos in this.table_offsets_fix)
					{
						bw.BaseStream.Position = pos.Key;
						bw.Write((uint)(item_offset + pos.Value));
					}
					bw.BaseStream.Position = item_offset;
					bw.Write(this.table_index.ToArray());
				}

				bw.Write(1836436848);

				fs.Seek(12, SeekOrigin.Begin);
				bw.Write(floatOffset);
				fs.Seek(28, SeekOrigin.Begin);
				bw.Write(stringOffset);
				fs.Seek(44, SeekOrigin.Begin);
				bw.Write(vectorOffset);
				fs.Seek(60, SeekOrigin.Begin);
				bw.Write(quaternionOffset);
				fs.Seek(76, SeekOrigin.Begin);
				bw.Write(idstringOffset);
				fs.Seek(92, SeekOrigin.Begin);
				bw.Write(tableOffset);

			}
		}

		private int getItemType(object input)
		{
			ulong tryp;
			if (input is Dictionary<string, object>)
				return 8;
			else if (input is double || input is float)
				return 3;
			else if (input is string)
			{
				if (this.strings.Contains(input as string))
					return 4;
				else
				{
					float floattest;
					if (float.TryParse(input as string, NumberStyles.Float, CultureInfo.InvariantCulture, out floattest))
					{
						return 3;
					}
				}
				return 4;
			}
			else if (input is float[] || input is Single[])
			{
				float[] temp = (float[])input;
				if (temp.Length == 3)
					return 5;
				else if (temp.Length == 4)
					return 6;
				else
					return -1;
			}
			else if (ulong.TryParse(input.ToString(), out tryp) || input is ulong)
			{
				return 7;
			}
			else if (input is bool)
				return Convert.ToInt32((bool)input) + 1;
			else
				return 0;
		}

		private bool listVectorContains(ref List<float[]> vectors, ref float[] data)
		{
			foreach (float[] vector in vectors)
			{
				if (vector[0] == data[0] && vector[1] == data[1] && vector[2] == data[2])
					return true;
			}
			return false;
		}

		private int listVectorLastIndex(ref List<float[]> vectors, ref float[] data)
		{
			foreach (float[] vector in vectors)
			{
				if (vector[0] == data[0] && vector[1] == data[1] && vector[2] == data[2])
					return vectors.LastIndexOf(vector);
			}
			return -1;
		}

		private bool listQuaternionContains(ref List<float[]> quaternions, ref float[] data)
		{
			foreach (float[] vector in quaternions)
			{
				if (vector[0] == data[0] && vector[1] == data[1] && vector[2] == data[2] && vector[3] == data[3])
					return true;
			}
			return false;
		}

		private int listQuaternionLastIndex(ref List<float[]> quaternions, ref float[] data)
		{
			foreach (float[] vector in quaternions)
			{
				if (vector[0] == data[0] && vector[1] == data[1] && vector[2] == data[2] && vector[3] == data[3])
					return quaternions.LastIndexOf(vector);
			}
			return -1;
		}



		private bool listContains(ref List<Dictionary<string, object>> tables, ref Dictionary<string, object> data)
		{
			foreach (var item in tables)
			{
				if (item.Count == data.Count)
				{
					bool broken = false;
					foreach (KeyValuePair<string, object> keypair in item)
					{
						
						if (!data.ContainsKey(keypair.Key))
						{
							broken = true;
							break;
						}

						if (!compareObjects(keypair.Value, data[keypair.Key]))
						{
							broken = true;
							break;
						}
					}
					if(!broken)
						return true;
				}
			}

			return false;
		}


		private int listLastIndex(ref List<Dictionary<string, object>> tables, ref Dictionary<string, object> data)
		{
			foreach (Dictionary<string, object> item in tables)
			{
				if (item.Count == data.Count)
				{
					bool broken = false;
					foreach (KeyValuePair<string, object> keypair in item)
					{
						if (!data.ContainsKey(keypair.Key))
						{
							broken = true;
							break;
						}

						if (!compareObjects(keypair.Value, data[keypair.Key]))
						{
							broken = true;
							break;
						}

					}
					if (!broken)
						return tables.IndexOf(item);
				}
			}

			return -1;
		}


		private bool compareObjects(object first, object second)
		{

			if (first is Dictionary<string, object> && second is Dictionary<string, object>)
			{
				Dictionary<string, object> firsttemp = (Dictionary<string, object>)first;
				Dictionary<string, object> secondtemp = (Dictionary<string, object>)second;

				if (firsttemp.Count != secondtemp.Count)
				{
					return false;
				}
				else
				{

					foreach (KeyValuePair<string, object> keypair in firsttemp)
					{

						if (secondtemp.ContainsKey(keypair.Key))
						{
							if (!compareObjects(firsttemp[keypair.Key], secondtemp[keypair.Key]))
							{
								return false;
							}
						}
						else
						{
							return false;
						}

					}

					return true;

				}
				
			}
			else if ((first is double && second is double) || (first is float && second is float))
			{
				if (Convert.ToSingle(first) == Convert.ToSingle(second))
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			else if ((first is string && second is string))
			{
				if (((string)first).Equals((string)second))
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			else if ((first is Single[] && second is Single[]) || (first is float[] && second is float[]))
			{
				float[] firsttemp = (float[])first;
				float[] secondtemp = (float[])second;

				if (firsttemp.Length == secondtemp.Length)
				{
					for (int x = 0; x < firsttemp.Length; x++)
					{
						if (firsttemp[x] != secondtemp[x])
							return false;
					}
					return true;

				}
				else
				{
					return false;
				}
			}
			else if ((first is ulong && second is ulong))
			{
				if((ulong)first == (ulong)second)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			else if ((first is bool && second is bool))
			{
				if ((bool)first == (bool)second)
				{
					return true;
				}
				else
				{
					return false;
				}
			}
			else
			{
				return false;
			}

		}


		private void organize(ref Dictionary<string, object> data, ref List<float> floats, ref List<string> strings, ref List<float[]> vectors, ref List<float[]> quaternions, ref List<ulong> idstrings, ref List<Dictionary<string, object>> tables)
		{
			bool organizeTable = false;   
			
			foreach (KeyValuePair<string, object> item in data)
			{

				if (item.Value is Dictionary<string, object>)
				{
					Dictionary<string, object> temp = (Dictionary<string, object>)(item.Value);
					organize(ref temp, ref floats, ref strings, ref vectors, ref quaternions, ref idstrings, ref tables);
					if (!listContains(ref tables, ref temp) || !organizeTable)
						tables.Add((Dictionary<string, object>)item.Value);
				}
				else if (item.Value is double)
				{
					if (!floats.Contains(Convert.ToSingle((double)item.Value)))
						floats.Add(Convert.ToSingle((double)item.Value));
				}
				else if (item.Value is Single)
				{
					if (!floats.Contains(Convert.ToSingle(item.Value)))
						floats.Add(Convert.ToSingle(item.Value));
				}
				else if (item.Value is string)
				{
					float floattest;
					if (float.TryParse((string)item.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out floattest))
					{
						if (!floats.Contains(floattest))
							floats.Add(floattest);
					}
					if (!strings.Contains((string)item.Value))
						strings.Add((string)item.Value);
				}
				else if (item.Value is Single[])
				{
					float[] temp = (float[])item.Value;
					if (temp.Length == 3 && !listVectorContains(ref vectors, ref temp))
						vectors.Add(temp);
					else if (temp.Length == 4 && !listQuaternionContains(ref quaternions, ref temp))
						quaternions.Add(temp);
				}
				else if (item.Value is UInt64)
				{
					if (!idstrings.Contains((ulong)item.Value))
						idstrings.Add((ulong)item.Value);
				}
				else if (item.Value is bool)
				{
				}
				else
				{
					ulong tryp;
					if (ulong.TryParse(item.Value.ToString(), out tryp))
					{
						if (!idstrings.Contains(tryp))
							idstrings.Add(tryp);
					}
					else
					{
						Console.WriteLine(item.Value.GetType());
					}
				}

				float floatparse;
				ulong ulongparse;
				if (float.TryParse(item.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out floatparse))
				{
					if (!floats.Contains(floatparse))
						floats.Add(floatparse);
				}
				else if (ulong.TryParse(item.Key, out ulongparse))
				{
					if (!idstrings.Contains(ulongparse))
						idstrings.Add(ulongparse);
				}
				else
				{
					if (!strings.Contains(item.Key))
						strings.Add(item.Key);
				}
			}
		}
		#endregion
	}
}