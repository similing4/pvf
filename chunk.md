int uuidLength; //UUID长度
char[uuidLength] uuid; //UUID
int fileVersion; //pvf文件版本
int dirTreeLength; //文件树大小
int dirTreeCrc32; //文件树解密CRC32
int headerFilesCount; //文件树包含的文件数
byte[dirTreeLength] unpackedHeaderTree; //加密后的文件树
byte[] filePack; //文件chunk，由文件树提供偏移量

解密：将0x81A79011与CRC32(这里是dirTreeCrc32)进行异或，而后逐4字节读取待解密块（这里是unpackedHeaderTree）并对其进行异或，并将后六位提到最开头以此解密。
加密：将0x81A79011与CRC32进行异或获得秘钥A，而后逐4字节读取解密后的待加密块，将前6位放置到最后，而后将处理过后的待加密块与秘钥A进行异或即可得到加密块。


unpackedHeaderTree 解密后的数据为连续存放的文件树，结构如下：
struct HeaderTree{
	int fileNumber; //文件编号
	int filePathLength; //文件完整路径长度
	byte[filePathLength] filePath; //文件完整路径 需要使用CP949（韩语）编码进行解码得到路径字符串，其路径以\为路径分隔符，。
	int fileLength; //文件字节数大小
	int fileCrc32; //文件解密CRC32
	int relativeOffset; //该文件相对filePack——文件chunk的偏移
}

文件chunk解密方法与文件树解密方法一致，不过CRC32秘钥换成了fileCrc32，被解密的文件chunk长度应当为4的倍数(fileLength + 3) & 0xFFFFFFFC
解密后最后多出来的几位应置为0

然后当解密到“stringtable.bin”文件的时候：

int stringTableLen; //字符串表长度
struct{
	int strChunkStart; //相对于stringtable.bin的文件解密chunk，字符串chunk开始位置的偏移量
	int strChunkEnd; //相对于stringtable.bin的文件解密chunk，字符串chunk结束位置的偏移量
}StringTableStrIndex[stringTableLen]; //字符串树（指明字符串的位置，编号从0开始），解密所得字符串为BIG5编码。
byte[] stringTableChunk; //字符串表chunk

当解密到“n_string.lst”文件的时候：
byte[2] vercode; //前两位为0xB0 0xD0
struct{
    byte a; //固定为0x2，不知何意
    int index; //StringTable的ID
    byte b; //固定为0x7，不知何意
    int StringTableStrIndexIndex; //对应StringTableStrIndex中的数组下标
}nStringList[]; //直至文件结束
逐个找到nStringList对应的文件后使用BIG5编码直接读取其二进制解密后的文件，并使用\r\n作为换行符逐行读取这些文件。前面几行一般是注释，一般trim后会以//开头。正常每行格式为：
growtype_name_0>格斗家
把growtype_name_0、格斗家两项逐对读取出来备用。
假定我们建立的结构为：
struct{
    int index; //StringTable的ID
    List<String,String> nStringKeyValPairList; //growtype_name_0>格斗家的键值对数组
}nStringObjectList[];


pvf指定文件读取：
对于byte[]的解密后的chunk，前两位不知为什么意思，弄了几个pvf发现都是固定的b0d0。
从第三位开始，chunk会按照1个字节，一个int如此的顺序放置。也就是这个字节相对解密后的chunk的偏移为2,7,12,17....
这个字节对应不同数字有不同含义。分别对应pvf文件中的如下语句：
定义逐个字节为currentByte
定义这个字节前面的int数为bef
定义这个字节后面的int数为aft
定义StringTable表的第aft个字符串为str
定义“n_string.lst”文件的nStringObjectList为nStringObjectList

*无论什么文件，都先来一个《#PVF_File\n》

*当字节为0xA时追加以下内容：
"<" + bef + "::" + (str) + "`" + (在nStringObjectList[bef].nStringKeyValPairList中搜索键为str的值) +"`>\r\n"

*当字节为0x7时追加以下内容：
"`" + str + "`\r\n"

*当字节为0x5时追加以下内容：
"\r\n", str, "\r\n"

当字节为0x2时追加以下内容：
aft + "\t"

当字节为0x4时追加以下内容：
读取aft的四位字节为32位单精度浮点数（显示时保留6位小数） + "\t"

当字节为0x6或0x8时追加以下内容：
"{" + currentByte, "=`" + str + "`}\r\n"

