using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace pvfLoaderXinyu
{
    class Pvf
    {
        PvfHeader header;//PVF文件头
        FileStream fs;//文件流，用于读取pvf文件
        public Dictionary<string, HeaderTreeNode> headerTreeCache = new Dictionary<string, HeaderTreeNode>();//文件索引字典，键为文件完整路径，值为文件索引对象
        public Dictionary<int, string> stringBinMap = new Dictionary<int, string>(); //stringtable.bin文件的索引键值关系，键为数字索引，值为字符串
        public Dictionary<string, string> nStringMap = new Dictionary<string, string>();//n_string.lst文件的索引键值关系，键值均为字符串
        /*
            pvf共分三部分：Header头，Tree文件索引头，文件内容数据，本类中：
            使用struct PvfHeader{}来存储Header头，在本类中为header对象
            使用Dictionary<string, HeaderTreeNode> headerTreeCache来存储Tree文件索引头，键为文件索引的文件名，值为索引对象
            使用HeaderTreeNode的索引对象存储文件内容，详情请看本类构造方法。
        */
        public Pvf(string file)//本构造方法使用后需dispose释放内存，构造过程请放到线程中异步调用。
        {
            fs = new FileStream(file, FileMode.Open);//打开文件
            header = (PvfHeader)Util.readFileAsType(fs, typeof(PvfHeader));//读取pvf文件头结构体到header变量
            int headLength = header.dirTreeLength;//获取文件索引列表字节总大小
            byte[] decryptedTree = new byte[header.dirTreeLength];//分配内存
            fs.Read(decryptedTree, 0,header.dirTreeLength);//读取文件索引列表
            Util.unpackHeaderTree(ref decryptedTree, header.dirTreeLength, (uint)header.dirTreeChecksum);//解密，解密后的字节数组为decryptedTree
            int pos = 0;//模拟读取字节数组的指针
            for (int i = 0; i < header.numFilesInDirTree; i++)
            {
                HeaderTreeNode item = new HeaderTreeNode();
                int a = item.readNodeFromBitArrStream(header,fs,decryptedTree,pos);//从pos位置开始读取HeaderTreeNode对象，返回值为指针应该偏移的字节数
                if (a < 0)
                    throw new Exception("读取错误，格式非法");//读取错误直接报错
                pos += a;//指针后移
                headerTreeCache[item.filePathName] = item;//把对象放入字典，以文件名为键
            }
            loadStringTableBin(headerTreeCache["stringtable.bin"].unpackedFileByteArr);//读取stringtable.bin文件创建stringtable索引
            loadNStringLst(headerTreeCache["n_string.lst"].unpackedFileByteArr);//读取n_string.lst文件创建n_string索引
            return;
        }
        private void loadStringTableBin(byte[] unpackedFileByteArr)
        {

            int count = BitConverter.ToInt32(unpackedFileByteArr, 0);//文件的第一位int是索引总数
            for (int i = 0; i < count; i++)
            {
                int startpos = BitConverter.ToInt32(unpackedFileByteArr, i * 4 + 4);//每次循环的第一个int是键开始的地址
                int endpos = BitConverter.ToInt32(unpackedFileByteArr, i * 4 + 8);//每次循环的第二个int是键结束的地址
                int len = endpos - startpos;//相减就是值的长度
                int index = i;//索引就是出现的第几个
                var pathBytes = new byte[len];//分配内存以存储该值的字符串
                Array.Copy(unpackedFileByteArr, startpos + 4, pathBytes, 0, len);//取出该字符串内容
                string pathName = Encoding.GetEncoding("BIG5").GetString(pathBytes).TrimEnd(new char[1]);//解码，这里使用的是BIG5，对于某些文件不一定正确，如果需要更正可以在这个编码这里下手。
                stringBinMap[index] = pathName;//放到索引表中备用
            }
        }
        private void loadNStringLst(byte[] unpackedFileByteArr)
        {
            if (BitConverter.ToUInt16(unpackedFileByteArr, 0) != 53424)//第一位一定是53424，如果不是那你pvf有问题
                return;
            for (int i = 2; i < unpackedFileByteArr.Length; i += 10)//从第二位开始每次读取十个字节
            {
                if (unpackedFileByteArr.Length - i >= 10)//如果是最后十个字节或者最后不满十个字节就不执行
                {
                    string k = stringBinMap[BitConverter.ToInt32(unpackedFileByteArr, i + 6)];//前6位干嘛的不知道，6-10位的int值是stringtable的键，取出来
                    var node = headerTreeCache[k.ToLower().Trim()];//取出来的stringtable的值是文件列表的一个文件的文件名，不过使用了驼峰命名需要将其置为小写并清除空格。
                    if (node != null)//如果找到了这个文件
                    {
                        string full = Encoding.GetEncoding("BIG5").GetString(node.unpackedFileByteArr).TrimEnd(new char[1]);//直接用编码取这个文件的内容
                        foreach (string line in full.Split(new char[2] { '\r', '\n' }))//根据换行分割，逐行遍历
                        {
                            if (line.IndexOf('>') >= 0)//行包含符号'>'，如name_xxx>格斗家
                            {
                                string key = Util.findTagKeyVal(line, "", ">");//取键 name_xxx
                                string val = Util.findTagKeyVal(line, ">", "");//取值 格斗家
                                if (key.Length > 0 && val.Length > 0)
                                    nStringMap[key] = val;//放到索引表中备用
                            }
                        }
                    }
                }
            }
            return;
        }
        public string getPvfFileByPath(string path, Encoding encoding)//根据文件名返回文件内容
        {
            var node = headerTreeCache[path.ToLower().Trim()];
            if (node == null)
                return null;
            return getPvfFileByPath(node, encoding);
        }
        public string getPvfFileByPath(HeaderTreeNode node, Encoding encoding)//根据文件索引对象返回文件内容
        {
            byte[] unpackedStrBytes = node.unpackedFileByteArr;//直接取解密内容
            //byte[] numArray = new byte[52428800];
            int strpos = 0;//导出文本的偏移指针
            Dictionary<int, byte[]> arr = new Dictionary<int, byte[]>();//导出文本的字典，键为偏移，值为字符串的字节形式，如0=>字符串AAA,3=>字符串BBB，整个字符串为AAABBB，AAA的基址为0，BBB的基址为3
            var bts = encoding.GetBytes("#PVF_File\r\n");
            arr.Add(strpos, bts);//开头加上一行#PVF_File
            strpos = bts.Length;//指针向后移动
            //文件结构为：byte[2](0xb0,0xd0打开好多文件这里都是这个值，猜测应该是固定的)byte[1]int[1]byte[1]int[1]byte[1]...以此循环，byte[1]为指示符，int[1]占四位为一个数字，具体意义需要看指示位
            if (unpackedStrBytes.Length >= 7)//如果总字节长度>=7
            {
                for (int i = 2; i < unpackedStrBytes.Length; i += 5)//以5为单步从第二位开始遍历字节
                {
                    //string s = encoding.GetString(numArray).TrimEnd(new char[1]);
                    if (unpackedStrBytes.Length - i >= 5)//到最后了就不处理了防止内存越界
                    {
                        byte currentByte = unpackedStrBytes[i];//猜测应该是内容指示位
                        if (currentByte == 2 || currentByte == 4 || currentByte == 5 || currentByte == 6 || currentByte == 7 || currentByte == 8 || currentByte == 10)
                        //如果这个字节是这些中的一个进行对应的特殊处理，如果不是那就没有字符串
                        {
                            int after1 = BitConverter.ToInt32(unpackedStrBytes, i + 1);//取该指示位后面的整数
                            if (currentByte == 10)//这个字符是10时
                            {
                                int before1 = BitConverter.ToInt32(unpackedStrBytes, i - 4);//取指示位前面的整数
                                //解释字符串内容的方法已集成到unpackSpecialChr(指示位,后一位整数,前一位整数)中
                                bts = Encoding.UTF8.GetBytes(string.Concat(unpackSpecialChr(currentByte, after1, before1), "\r\n"));//获取该指示位代表的字符串
                                arr.Add(strpos, bts);
                                strpos += bts.Length;
                            }
                            else if (currentByte == 7)//这个字符是7时
                            {
                                bts = Encoding.UTF8.GetBytes(string.Concat("`", unpackSpecialChr(currentByte, after1, 0), "`\r\n"));//7不需要前一位整数，外面要套上“``”
                                arr.Add(strpos, bts);
                                strpos += bts.Length;
                            }
                            else if (currentByte == 2 || currentByte == 4)//这个字符是2或者4时，末尾不是换行而是制表符\t
                            {
                                bts = Encoding.UTF8.GetBytes(string.Concat(unpackSpecialChr(currentByte, after1, 0), "\t"));
                                arr.Add(strpos, bts);
                                strpos += bts.Length;
                            }
                            else if(currentByte == 6 || currentByte == 8)//{指示位=`stringbin[后面的整数]`}
                            {
                                string[] str = new string[] { "{", currentByte.ToString(), "=`", unpackSpecialChr(currentByte, after1, 0), "`}\r\n" };
                                bts = encoding.GetBytes(string.Concat(str));
                                arr.Add(strpos, bts);
                                strpos += bts.Length;
                            }
                            else if (currentByte == 5) //是5的情况，stringbin[后面的整数]
                            {
                                bts = Encoding.UTF8.GetBytes(string.Concat("\r\n", unpackSpecialChr(currentByte, after1, 0), "\r\n"));
                                arr.Add(strpos, bts);
                                strpos += bts.Length;
                            }
                        }
                    }
                }
                bts = encoding.GetBytes("\r\n");//末尾添个换行符
                arr.Add(strpos, bts);
                strpos += bts.Length;
            }
            byte[] bytes = new byte[strpos];//创建一个正好大小的字节数组
            foreach (int pos in arr.Keys)
                for (int i = 0; i < arr[pos].Length; i++)
                    bytes[i + pos] = arr[pos][i];//使用导出文本的字典填充字节数组
            arr.Clear();//释放内存
            string str1 = encoding.GetString(bytes).TrimEnd(new char[1]);//转换成文本
            return str1;
        }

        private string unpackSpecialChr(byte curr, int after1, int before1)
        {
            switch (curr)
            {
                case 2:
                    return after1.ToString();//int型数
                case 4:
                    return BitConverter.ToSingle(BitConverter.GetBytes(after1), 0).ToString("f6");//4为float型数
                case 5:
                case 6:
                case 7:
                case 8:
                    if (stringBinMap.ContainsKey(after1))
                        return stringBinMap[after1];//字符串类型，字符串数据存在stringtable.bin里
                    return "";
                case 10:
                    if (!stringBinMap.ContainsKey(after1))
                        return "";
                    string str = stringBinMap[after1];//字符串类型，字符串数据存在stringtable.bin里
                    string[] strArrays = new string[] { "<", before1.ToString(), "::", str, "`", getNString(before1, str), "`>" };//<指示符前面的数字::字符串`nstring[指示符前面的数字]`>
                    return string.Concat(strArrays);
            }
            return "";
        }

        private string getNString(int before4, string str)
        {
            if (nStringMap.ContainsKey(str))
                return nStringMap[str];
            return "";
        }

        public void dispose()
        {
            headerTreeCache = new Dictionary<string, HeaderTreeNode>();
            stringBinMap = new Dictionary<int, string>();
            nStringMap = new Dictionary<string, string>();
            fs.Close();
            fs = null;
        }
    }
}
