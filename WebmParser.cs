using ImageProcessor.Processors;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace tagVideoManager
{
	internal class WebmParser
	{
		private static readonly byte[] Segment = new byte[] { 0x18, 0x53, 0x80, 0x67 }; // segment
		private static readonly byte[] Cluster = new byte[] { 0x1F, 0x43, 0xB6, 0x75 }; // cluster
		

		// バイト配列が任意のものか判定
		private static bool IsSameId(List<byte> id, byte[] values)
		{
			if (id.Count != values.Length)
				return false;

			for (int i = 0; i < values.Length; i++)
			{
				if (id[i] != values[i])
					return false;
			}

			return true;
		}

		// VINTから残りのデータバイト数を取得
		private static int GetVintSize(byte value, out byte mask)
		{
			byte hitBit = 0b10000000;
			var count = 0;
			for (; count < 8; count++)
			{
				if ((hitBit & value) != 0)
					break;

				hitBit >>= 1;
			}

			mask = (byte)(~hitBit);
			return count;
		}

		private static List<byte> GetId(BinaryReader reader)
		{
			var result = new List<byte>();
			var key = (byte)reader.ReadByte();
			result.Add(key);
			byte mask = 0;
			var count = GetVintSize(key, out mask);
			for (int i = 0; i < count; i++)
			{
				result.Add((byte)reader.ReadByte());
			}

			return result;
		}

		private static long WriteDataSize(BinaryReader reader, BinaryWriter writer = null)
		{
			var key = (byte)reader.ReadByte();
			writer?.Write(key);

			byte mask = 0;
			var count = GetVintSize(key, out mask);

			long size = mask & key;
			for (int i = 0; i < count; i++)
			{
				var val = (byte)reader.ReadByte();
				writer?.Write(val);
				size = size * 256 + val;
			}

			return size;
		}


		// データを出力
		private static long WriteData(BinaryReader reader, BinaryWriter writer = null)
		{
			var size = WriteDataSize(reader, writer);
			if (writer != null)
			{
				writer.Write(reader.ReadBytes((int)size));
			}
			else
			{
				reader.BaseStream.Position += size;
			}
			return size;
		}

		//セグメントの中まで出力
		public static bool WriteSegmentIn(BinaryReader reader, BinaryWriter writer = null)
		{
			while (reader.BaseStream.Position < reader.BaseStream.Length)
			{
				var id = GetId(reader);
				writer?.Write(id.ToArray(), 0, id.Count);

				// Segmentなら内部を見ていく
				if (IsSameId(id, Segment))
				{
					WriteDataSize(reader, writer);
					return true;
				}

				WriteData(reader, writer);
			}

			return false;	// 見つからなかった
		}



		public static void Convert(BinaryWriter writer, BinaryReader reader, bool init)
		{
			//セグメントの中まで移動
			if (!WriteSegmentIn(reader, init ? writer : null))
				return;


			while (reader.BaseStream.Position < reader.BaseStream.Length)
			{
				var id = GetId(reader);

				if (init)
				{
					// クラスターまで
					if (IsSameId(id, Cluster))
						break;
				}
				else
				{
					// クラスターから
					if (!IsSameId(id, Cluster))
					{
						WriteData(reader);
						continue;
					}
				}

				// id出力
				writer.Write(id.ToArray(), 0, id.Count);
				// データ出力
				var size = WriteData(reader, writer);
				Console.WriteLine($"out id[{DebugGetIdString(id)}] size:{size}");
			}
		}


		public static string DebugGetIdString(List<byte> list)
		{
			var result = "";
			foreach (var item in list)
			{
				result += $"{item:X2}";
			}

			return result;
		}


		public static void DebugLog(BinaryReader reader)
		{
			while(reader.BaseStream.Position < reader.BaseStream.Length)
			{
				var id = GetId(reader);
				Console.Write($"id[{DebugGetIdString(id)}");

				// セグメントならサイズ分だけ進めて中身を解析
				if (IsSameId(id, Segment))
				{
					var segSize = WriteDataSize(reader);
					Console.WriteLine($"] size:{segSize} // segment!");
					continue;
				}
				
				// データをスキップして次のブロックへ
				var size = WriteData(reader);
				Console.WriteLine($"] size:{size}");
			}
		}

		public static void DebugLog(string path)
		{
			using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				using (var rs = new BinaryReader(fs))
				{
					DebugLog(rs);
				}
			}
		}
	}
}
