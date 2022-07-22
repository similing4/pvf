# 1.PVF文件基本chunk
任何未加密的PVF二进制文件chunk格式均为如下
```c
int uuidLength; //UUID长度
char[uuidLength] uuid; //UUID
int fileVersion; //pvf文件版本
int dirTreeLength; //文件树大小
int dirTreeCrc32; //文件树解密CRC32
int headerFilesCount; //文件树包含的文件数
byte[dirTreeLength] unpackedHeaderTree; //加密后的文件树
byte[] filePack; //文件chunk，由文件树提供偏移量
```

# 2.文件解密算法

## 2.1解密：
将0x81A79011与CRC32(这里是dirTreeCrc32)进行异或，而后逐4字节读取待解密块（这里是unpackedHeaderTree）并对其进行异或，并将后六位提到最开头以此解密。
示例:
```C#
void unpackHeaderTree(ref byte[] byteArr, int fileLen, uint crc32) {
	uint key = 2175242257;// 1000 0001 1010 0111 1001 0000 0001 0001，即0x81A79011
	//传入CRC范例：0110 1111 1111 0100 1010 1101 1000 0001
	int index = 0; //当前下标
	while (index < fileLen) { //逐4字节遍历所有字节
		int num = BitConverter.ToUInt32(byteArr, index) ^ key ^ crc32; //将读取的4字节int数与key^CRC进行异或运算。因为异或运算有交换律所以位置随意。
		Array.Copy(BitConverter.GetBytes(num >> 6 | num << 32 - 6), 0, byteArr, index, 4); //将异或运算得到的int数的二进制后六位放到前面，前面26位放到后面，然后把解密后的数据塞到原位置。
                index += 4;//后移四位解密后面四位
	}
}
```

## 2.2加密：
由于异或有自反性，也就是A^B^B == A，所以对上述解密内容进行逆运算即可进行加密。
将0x81A79011与CRC32进行异或获得秘钥A，而后逐4字节读取解密后的待加密块，将前6位放置到最后，而后将处理过后的待加密块与秘钥A进行异或即可得到加密块。

示例：
```C#
void packHeaderTree(ref byte[] byteArr, int fileLen, uint crc32) {
	uint key = 2175242257;// 1000 0001 1010 0111 1001 0000 0001 0001，即0x81A79011
	//传入CRC范例：0110 1111 1111 0100 1010 1101 1000 0001
	int index = 0; //当前下标
	while (index < fileLen) { //逐4字节遍历所有字节
		int num = BitConverter.ToUInt32(byteArr, index); //读取
		num = num >> 32 - 6 | num << 6; //将解密对应的位置返还回来
		num = num ^ key ^ crc32; //将这4字节int数与key^CRC进行异或运算，由其自反性可获得源加密字符串。
		Array.Copy(BitConverter.GetBytes(num), 0, byteArr, index, 4); //把加密后的数据塞到原位置。
                index += 4;//后移四位加密后面四位
	}
}
```

## 2.3实例：
```
解密二进制示例：
假定待解密字符串其中四个字节如下：
1001 0010 0111 0110 0011 1011 0101 0011 
0x81A79011的二进制如下：
1000 0001 1010 0111 1001 0000 0001 0001
传入CRC范例如下：
0110 1111 1111 0100 1010 1101 1000 0001

那么解密过程：
1001 0010 0111 0110 0011 1011 0101 0011 // 待解密串
^
1000 0001 1010 0111 1001 0000 0001 0001 // 与0x81A79011异或
=
0001 0011 1101 0001 1010 1011 0100 0010 // 待解密串与0x81A79011异或的结果

0001 0011 1101 0001 1010 1011 0100 0010 // 待解密串与0x81A79011异或的结果
^
0110 1111 1111 0100 1010 1101 1000 0001 // 与CRC32异或
=
0111 1100 0010 0101 0000 0110 1100 0011 // 待解密串与0x81A79011和CRC32异或的结果

将异或结果后6位“0110 1100 0011”提前，将剩余位置后：
0110 1100 0011 0111 1100 0010 0101 0000
这个就是解密结果啦

加密二进制示例：
待加密串拿刚刚的解密结果作为范例：
0110 1100 0011 0111 1100 0010 0101 0000
0x81A79011的二进制如下：
1000 0001 1010 0111 1001 0000 0001 0001
传入CRC范例如下：
0110 1111 1111 0100 1010 1101 1000 0001

那么加密过程：
将异或结果前6位“0110 1100 0011”置后，将剩余位置前：
0111 1100 0010 0101 0000 0110 1100 0011 // 置换后的串
^
1000 0001 1010 0111 1001 0000 0001 0001 // 与0x81A79011异或
=
1111 1101 1000 0010 1001 0110 1101 0010 // 置换后的串与0x81A79011异或的结果

1111 1101 1000 0010 1001 0110 1101 0010 // 置换后的串与0x81A79011异或的结果
^
0110 1111 1111 0100 1010 1101 1000 0001 // 与CRC32异或
=
1001 0010 0111 0110 0011 1011 0101 0011 // 这个就是加密结果了，和待解密的加密串一致
```

# 3.文件树chunk
《1.PVF文件基本chunk》中的unpackedHeaderTree使用《2.文件解密算法》解密后的数据为连续存放的文件树，结构如下：
```C
struct{
	int fileNumber; //文件编号
	int filePathLength; //文件完整路径长度
	byte[filePathLength] filePath; //文件完整路径 需要使用CP949（韩语）编码进行解码得到路径字符串，其路径以\为路径分隔符，。
	int fileLength; //文件字节数大小
	int fileCrc32; //文件解密CRC32
	int relativeOffset; //该文件相对filePack——文件chunk的偏移
}HeaderTree[];
```
我们根据relativeOffset和fileLength就可以知道文件的详细大小和chunk内容了~
对文件chunk使用《2.文件解密算法》解密，CRC32秘钥为fileCrc32，被解密的文件chunk长度应当为4的倍数(fileLength + 3) & 0xFFFFFFFC，解密后最后多出来的几位应置为0。

# 4.StringTable.BIN
stringtable.bin文件需要预先处理。对这个文件chunk使用《2.文件解密算法》解密后获得的解密文件chunk的数据结构如下：
```C
int stringTableLen; //字符串表长度
struct{
	int strChunkStart; //相对于stringtable.bin的文件解密chunk，字符串chunk开始位置的偏移量
	int strChunkEnd; //相对于stringtable.bin的文件解密chunk，字符串chunk结束位置的偏移量
}StringTableStrIndex[stringTableLen]; //字符串树（指明字符串的位置，编号从0开始），解密所得字符串为BIG5编码。
byte[] stringTableChunk; //字符串表chunk
```

# 5.N_String.LST
n_string.lst文件需要预先处理。对这个文件chunk使用《2.文件解密算法》解密后获得的解密文件chunk的数据结构如下：
``` C
byte[2] vercode; //前两位为0xB0 0xD0
struct{
    byte a; //固定为0x2
    int index; //StringTable的ID
    byte b; //固定为0x7
    int StringTableStrIndexIndex; //对应《4.StringTable.BIN》中StringTableStrIndex的数组下标
}nStringList[]; //直至文件结束
```

根据StringTableStrIndexIndex，逐个找到《4.StringTable.BIN》中StringTableStrIndex对应的字符串作为文件完整路径，使用BIG5编码直接读取这个文件完整路径对应其二进制解密后的文件，并使用\r\n作为换行符逐行读取这些文件。一般格式如下(其中的换行符为\r\n)：
```
// ??????? ??? ??? ??
//
// ★ ? ??? ??? ?? ? ???
// ★ ????? ??? ??? ??? ?? ???? ?????!
// ★ ??? ???? ???? ????? ??????,
// ★ ??? ??? ???? ?? ???? ??????!
//

// 060412
// Script\\Character
growtype_name_0>格斗家
growtype_name_5>氣功師
growtype_name_6>念帝
growtype_name_7>念帝
growtype_name_10>散打
growtype_name_11>極武聖
growtype_name_12>極武聖
growtype_name_15>街霸
growtype_name_16>毒神絕
growtype_name_17>毒神絕
growtype_name_20>柔道家
growtype_name_21>暴風女皇
growtype_name_22>暴風女皇
growtype_name_25>神槍手
growtype_name_30>漫遊槍手
growtype_name_31>掠天之翼
growtype_name_32>//待定
growtype_name_33>緋紅玫瑰
growtype_name_34>//待定
growtype_name_35>槍炮師
growtype_name_36>毀滅者
growtype_name_37>//待定
growtype_name_38>暴風騎兵
growtype_name_39>//待定
growtype_name_40>機械師
growtype_name_41>機械元首
growtype_name_42>//待定
growtype_name_43>機械之靈
growtype_name_44>//待定
growtype_name_45>彈藥專家
growtype_name_46>戰場統治者
growtype_name_47>//待定
growtype_name_48>芙蕾雅
growtype_name_49>//待定
growtype_name_50>魔法師
growtype_name_55>元素師
growtype_name_56>元素聖靈
growtype_name_57>//待定
growtype_name_60>召喚師
growtype_name_61>月蝕
growtype_name_62>//待定
growtype_name_65>戰斗法師
growtype_name_66>伊斯塔戰靈
growtype_name_67>//待定
growtype_name_70>魔道學者
growtype_name_71>古靈精怪
growtype_name_72>//待定
growtype_name_75>鬼劍士
growtype_name_80>劍魂
growtype_name_81>劍神
growtype_name_82>劍神
growtype_name_85>鬼泣
growtype_name_86>黑暗君主
growtype_name_87>黑暗君主
growtype_name_90>狂戰士
growtype_name_91>帝血弒天
growtype_name_92>帝血弒天
growtype_name_95>阿修羅
growtype_name_96>天帝
growtype_name_97>天帝
growtype_name_100>聖職者
growtype_name_101>//待定
growtype_name_102>//待定
growtype_name_105>聖騎士
growtype_name_106>神思者
growtype_name_107>//待定
growtype_name_110>藍拳聖使
growtype_name_111>正義仲裁者
growtype_name_112>//待定
growtype_name_115>驅魔師
growtype_name_116>真龍星君
growtype_name_117>//待定
growtype_name_120>復仇者
growtype_name_121>永生者
growtype_name_122>念皇
growtype_name_123>//待定
growtype_name_124>暗街之王
growtype_name_125>//待定
growtype_name_126>極武皇
growtype_name_127>//待定
growtype_name_128>宗師
growtype_name_129>//待定
growtype_name_130>元素爆破師
growtype_name_131>湮滅之瞳
growtype_name_132>//待定
growtype_name_133>冰結師
growtype_name_134>剎那永恆
growtype_name_135>//待定

// ?? ??
growtype_name_140>暗夜使者
growtype_name_141>//待定
growtype_name_142>//待定
growtype_name_145>刺客
growtype_name_146>月影星劫
growtype_name_147>//待定
growtype_name_150>死靈術士
growtype_name_151>亡靈主宰
growtype_name_152>//待定
growtype_name_155>忍者
growtype_name_156>//待定
growtype_name_157>//待定
growtype_name_160>影武者
growtype_name_161>//待定
growtype_name_162>//待定

//?5?? ??
growtype_name_171>征服者
growtype_name_172>步兵
growtype_name_173>金剛力士


//?? ??? ???
growtype_name_200>黑暗武士
growtype_name_201>締造者
growtype_name_202>雙影

//????
alchemist_1>笨手笨腳的煉金術師
alchemist_2>激情澎湃的煉金術師
alchemist_3>潛力爆發的煉金術師
alchemist_4>頑固之煉金術師
alchemist_5>宮廷煉金術師
alchemist_6>火花之煉金術師
alchemist_7>黃金之煉金術師
alchemist_8>秘銀之煉金術師
alchemist_9>生命之煉金術師
alchemist_10>遠古之煉金術師
alchemist_11>超神之煉金術師

disjointer_1>滿身傷痕的分解師
disjointer_2>滿身油污的分解師
disjointer_3>滿身大汗的分解師
disjointer_4>從容不迫的分解師
disjointer_5>粗壯臂彎的分解師
disjointer_6>宮廷分解師
disjointer_7>史上最強分解師
disjointer_8>超越極限的分解師
disjointer_9>輝煌成就的分解師
disjointer_10>萬人景仰的分解師
disjointer_11>絕無僅有的分解師

doll_master_1>呆頭呆腦的控偶師
doll_master_2>菜鳥控偶師
doll_master_3>倒楣熊控偶師
doll_master_4>狒狒控偶師
doll_master_5>泰迪熊控偶師
doll_master_6>遠古之控偶師
doll_master_7>驅鬼之控偶師
doll_master_8>精巧之控偶師
doll_master_9>靈性之控偶師
doll_master_10>靈魂之控偶師
doll_master_11>創世之控偶師


enchanter_1>超級菜鳥附魔師
enchanter_2>誠實的附魔師
enchanter_3>隱隱發光的附魔師
enchanter_4>藍月之附魔師
enchanter_5>清澈瞳眸的附魔師
enchanter_6>偉大幻想之附魔師
enchanter_7>大自然之附魔師
enchanter_8>命運之附魔師
enchanter_9>深刻領悟之附魔師
enchanter_10>神佑之附魔師
enchanter_11>傳奇之附魔師
```
前面几行一般是注释，一般trim后会以//开头。正常每行格式为：
growtype_name_0>格斗家
把growtype_name_0、格斗家两项逐对读取出来备用。
假定我们建立的结构为：
```C
struct{
    int index; //StringTable的ID
    List<String,String> nStringKeyValPairList; //growtype_name_0>格斗家的键值对数组
}nStringObjectList[];
```


# 6.pvf指定文件读取：
对这个文件chunk使用《2.文件解密算法》解密后获得的解密文件chunk的数据结构如下：
前2位字节固定为b0d0。
字节从第三位开始，会按照1个字节，一个Int数如此的顺序放置。也就是这个字节相对解密后的chunk的偏移为2,7,12,17以此类推。
其中那一个字节是标明后面的Int数据位(下称数据位)的类型的标志位。这个标志位意义如下：
```c#
enum ScriptType
{
	Int = 2, //数据位是Int的二进制表示
	IntEx = 3, //数据位是Int的二进制表示
	Float = 4, //数据位是单精度浮点数的二进制表示
	Section = 5, //数据位是stringtable.bin中文本下标（Int）的二进制表示
	Command = 6, //数据位是stringtable.bin中文本下标（Int）的二进制表示
	String = 7, //数据位是stringtable.bin中文本下标（Int）的二进制表示
	CommandSeparator = 8, //数据位是stringtable.bin中文本下标（Int）的二进制表示
	StringLinkIndex = 9, //数据位是n_string.lst中的文件名（文本）下标（Int）的二进制表示。表意时应取下一个标志位的数据（StringLink类型）作为取出文件名对应的lst文件的字符串下标
	StringLink = 10 //详见StringLinkIndex
}
```

我们假定一些变量：

定义标志位为currentByte

定义当前标志位的数据位为currentData

定义下一个标志位的数据位为afterData

定义stringtable.bin表的第n个字符串为stringtable\[n\]

定义n_string.lst表的第n个字符串为nStringObjectList\[n\]

*无论什么文件，都先来一个《#PVF_File\n》

## 6.1.当标志位为2(Int)时追加以下内容：
```
currentData + "\t"
```
## 6.2.当标志位为3(IntEx)时追加以下内容：
```
"{" + currentByte, "=`" + currentData + "`}
```
## 6.3.当标志位为4(Float)时追加以下内容：
```
读取currentData的四位字节为32位单精度浮点数（显示时保留6位小数） + "\t"
```
## 6.4.当标志位为5(Section)时追加以下内容：
```
"\r\n", stringtable[currentData], "\r\n"
```
## 6.5.当标志位为6(Command)或8(CommandSeparator)时追加以下内容：
```
"{" + currentByte, "=`" + stringtable[currentData] + "`}\r\n"
```
## 6.6.当标志位为7(String)时追加以下内容：
```
"`" + stringtable[currentData] + "`\r\n"
```
## 6.7.当标志位为9(StringLinkIndex)时追加以下内容：
```
"<" + currentByte + "::" + (stringtable[afterData]) + "`" + (nStringObjectList[currentData].nStringKeyValPairList[stringtable[afterData]]) +"`>\r\n"
```
## 6.8.当标志位为10(StringLink)时不做任何处理（因为6.7处理完了）

上述写法只是为了方便逆向存储，也是通用的pvf解析规则。
