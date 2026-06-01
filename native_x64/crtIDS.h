#pragma once

#include <string.h>
#include "c_api.h"

#ifdef __cplusplus
extern "C" {
#endif

	// 错误码
/*#define CRT_ERRCOUNT                 -200
#define CRT_ERR_CMD                  (CRT_ERRCOUNT-1) // 命令字错误
#define CRT_ERR_CMDPARAM             (CRT_ERRCOUNT-2) // 命令参数错误
#define CRT_ERR_CMDDENIAL            (CRT_ERRCOUNT-3) // 命令不能被执行
#define CRT_ERR_DEVNOTSUP            (CRT_ERRCOUNT-4) // 硬件不支持
*/

	typedef enum
	{
		EN_IDS_SHUTTER_FRONT = 0,				// 前端卡口
		EN_IDS_SHUTTER_BACK = 1					// 后端卡口
	}EN_IDS_SHUTTER_TYPE;

	typedef enum
	{
		EN_IDS_SHUTTER_ENABLE = 0,				// 允许进卡
		EN_IDS_SHUTTER_DISABLE = 1				// 禁止进卡
	}EN_IDS_SHUTTER_CAP;

	typedef enum
	{
		_IDS_MOVE_POS_INSERT = 0,				// 移到用户插卡方向
		_IDS_MOVE_POS_SCAN,						// 移到扫描准备位置
		_IDS_MOVE_POS_CALI,						// 移到校正准备位置
		_IDS_MOVE_POS_RECLAIM,					// 卡片回收方向
		_IDS_MOVE_POS_EJECT_HALF,				// 卡片RF位置
		_IDS_MOVE_POS_PERLOAD,					// 预加载卡片
		_IDS_MOVE_POS_SWALLOW,					// 吞卡
		_IDS_MOVE_POS_OUT,						// 吐卡
		_IDS_MOVE_POS_CPU						// 卡走到CPU位置
	}_IDS_MOVE_POS;

	// 扫描颜色
	typedef enum
	{
		_IDS_SCAN_MODE_COLOR = 0,				// 彩色模式
		_IDS_SCAN_MODE_GRAY_R,					// 灰度(R)
		_IDS_SCAN_MODE_GRAY_G,					// 灰度(G)
		_IDS_SCAN_MODE_GRAY_B,					// 灰度(B)
		_IDS_SCAN_MODE_GRAY_IR,					// 灰度(IR)
	}_IDS_SCAN_MODE;

	// 扫描面向
	typedef enum
	{
		_IDS_SCAN_SIDE_FRONT = 0x01,			// 上部扫描标示
		_IDS_SCAN_SIDE_BACK = 0x02,				// 下部扫描标示
		_IDS_SCAN_SIDE_DUP = 0x03				// 双面扫描标示
	}_IDS_SCAN_SIDE;

	// 扫描分辨率
	typedef enum
	{
		_IDS_SCAN_DPI_150 = 150,
		_IDS_SCAN_DPI_200 = 200,
		_IDS_SCAN_DPI_300 = 300,
		_IDS_SCAN_DPI_600 = 600
	}_IDS_SCAN_DPI;

	// 卡类型
	typedef enum
	{
		// 暂时无实际意义
		_IDS_CARD_TYPE_IMAGE = 1,				// 普通卡，仅扫描图像(纵向)
		_IDS_CARD_TYPE_ID_CN = 2,				// 中国居民身份证(含外国人居留证)[固定扫描区域&自动面向识别&自动裁切]
		_IDS_CARD_TYPE_PASS_CN = 3,				// 港澳台通行证[固定扫描区域&自动面向识别&自动裁切]
		_IDS_CARD_TYPE_ONLY_ID = 4,				// 只扫身份证，非身份证报错
		_IDS_CARD_TYPE_ONLY_ALIEN = 5			// 只扫外国人测试证件
	}_IDS_CARD_TYPE;

	typedef enum
	{
		_IDS_FILE_FORMAT_BMP = 0,
		_IDS_FILE_FORMAT_JPG,
	}_IDS_FILE_FORMAT;

	// 图像调整参数
	typedef struct
	{
		int lBrightness;						// 明亮度参数 0~100
		int lContrast;							// 对比度参数 0~100
		int lGamma;								// 影像Gamma调整曲线参数 0 ~ 150
	}_IDS_IMAGE_PARAM;

	// 扫描配置
	typedef struct
	{
		_IDS_CARD_TYPE     _Type;
		_IDS_SCAN_MODE     _Mode;
		_IDS_SCAN_SIDE     _Side;
		_IDS_SCAN_DPI      _Dpi;
		_IDS_IMAGE_PARAM   _Front;
		_IDS_IMAGE_PARAM   _Back;
	}_IDS_SCAN_CONF;

	typedef struct
	{
		char            cFileNameFront[260];	// 可见光正面图片
		char            cFileNameBack[260];		// 可见光背面图片
		unsigned char   ucCardDir;
		unsigned char   ucCardType;
		unsigned char   aucRsv[2];
	}_IDS_SCAN_RESULT;

	typedef struct
	{
		// _IDS_DEV_DS_STATUS _Sensor;  
		unsigned int ulCOVER;
		unsigned int ulSensor1;
		unsigned int ulSensor2;
		unsigned int ulSensor3;
		unsigned int ulSensor4;
		unsigned int ulSlpSta;
		unsigned int ulRsv;
	}_IDS_DEV_DS_STATUS;

	typedef struct
	{
		_IDS_DEV_DS_STATUS _Sensor;
	}_IDS_STATUS_RESULT;

	//----------------------身份证
	typedef enum
	{
		EN_RFCARD_TYPE_ID_ALIEN = 'I',				//2017版外国人居留证
		EN_RFCARD_TYPE_ID_HMT_RP = 'J',			//港澳台
		EN_RFCARD_TYPE_ID_ALIEN_NEW = 'Y',		//2023版新外国人居留证
		EN_RFCARD_TYPE_IC_BYYJ = 0x10,			//身份证
	}EN_RFCARD_TYPE;

	// 中国居民身份证读取信息
	typedef struct
	{
		unsigned char ucName[60];               // 姓名
		unsigned char ucGender[10];             // 性别
		unsigned char ucNation[100];            // 民族
		unsigned char ucBirth[16];              // 出生日期
		unsigned char ucAddress[100];           // 住址
		unsigned char ucIDNumber[36];           // 公民身份证号
		unsigned char ucIssued[80];             // 签发机关
		unsigned char ucDateBegin[16];          // 有效期起始日期
		unsigned char ucDateEnd[16];            // 有效期截止日期
		char          cHeadName[260];           // 指定路径中身份证头部图像文件名称
		char          cLeftName[260];           // 指定路径中左手指纹图像文件名称
		char          cRightName[260];          // 指定路径中右手指纹图像文件名称
		unsigned char	ucUIDLen;
		unsigned char  aucUID[32];
		char          cLeftData[512];           // 左手指纹数据
		char          cRightData[512];          // 右手指纹数据
	}_IDS_CARD_INFO_CN;

	//外国人居留证
	typedef struct
	{
		unsigned char ucName[240];              // 姓名
		unsigned char ucOtherEnName[240];       // 其他英文名
		unsigned char ucNameOfChina[100];       // 中文名
		unsigned char ucGender[10];             // 性别
		unsigned char ucNation[100];            // 民族
		unsigned char ucBirth[32];              // 出生日期    
		unsigned char ucIDNumber[60];           // 公民身份证号
		unsigned char ucIssued[80];             // 签发机关
		unsigned char ucDateBegin[32];          // 有效期起始日期
		unsigned char ucDateEnd[32];            // 有效期截止日期
		unsigned char ucVersion[8];             // 证件版本号
		unsigned char ucTypeId[4];              // 证件类型标示"I"
		unsigned char ucRsv[64];                // 预留项
		unsigned char ucOldVersion[16];         // 既往证件版本号关联
		unsigned char ucIssueTimes[8];          // 换证次数
		char          cHeadName[260];           // 指定路径中身份证头部图像文件名称
		char          cLeftName[260];           // 指定路径中左手指纹图像文件名称
		char          cRightName[260];          // 指定路径中右手指纹图像文件名称
		unsigned char ucUIDLen;
		unsigned char aucUID[32];
		char          cLeftData[512];           // 左手指纹数据
		char          cRightData[512];          // 右手指纹数据
	}_IDS_CARD_INFO_CN_ALIEN;

	//港澳台居住证
	typedef struct
	{
		unsigned char aucName[60];              //姓名
		unsigned char aucGender[10];            //性别
		unsigned char aucBirth[16];             //出生日期
		unsigned char aucAddress[140];          //住址
		unsigned char aucIDNumber[36];          //公民身份证号
		unsigned char aucIssued[80];            //签发机关
		unsigned char aucDateBegin[16];         //有效期起始日期
		unsigned char aucDateEnd[16];           //有效期截止日期
		unsigned char aucNo[36];                //通行证号码
		unsigned char aucIssuedTotal[8];        //签发次数
		unsigned char aucTypeId[4];             //证件类型标示"J"
		char          cHeadName[260];           //指定路径中身份证头部图像文件名称
		char          cLeftName[260];           //指定路径中左手指纹图像文件名称
		char          cRightName[260];          //指定路径中右手指纹图像文件名称
		unsigned char ucUIDLen;
		unsigned char aucUID[32];
		char          cLeftData[512];           //左手指纹数据
		char          cRightData[512];          //右手指纹数据
	}_IDS_CARD_INFO_CN_HMT_RP;

	typedef struct
	{
		unsigned int ulDataLen;
		unsigned char aucData[1000];
	}ST_CARD_INFO_IC_GEN, * PST_CARD_INFO_IC_GEN;

	typedef struct
	{
		unsigned char               ucType;     //证件类型
		union
		{
			unsigned char               aucRsv[1500];
			_IDS_CARD_INFO_CN           CardInfoCN;         // 中国居民身份证读取信息
			_IDS_CARD_INFO_CN_ALIEN     CardInfoCNAlien;    // 外国人居留证
			_IDS_CARD_INFO_CN_HMT_RP    CardInfoHMTRP;      // 港澳台公民居住证
			ST_CARD_INFO_IC_GEN         CardInfoICGen;
		};
	}_IDS_CARD_INFO_RF, _IDS_CARD_INFO_OCR;
	//-----------------------------

	struct TagInfo {
		unsigned char tag;						// 标签标识符(1字节)
		char name[16];							// 标签名称（中文描述）
		int length;								// 数据长度（解析后计算的实际长度）
		char value[8 * 4096];					// 原始二进制数据值
	};

	struct DGTagsData {
		TagInfo* tags;							// DG数据结构体
		size_t tagCount;						// 返回读取到DG个数
	};

	typedef enum
	{
		_IDS_RET_OK_Back = 1,					// 正常--反面存在mrz
		_IDS_RET_OK = 0,						// 正常--正面存在mrz
		_IDS_RET_ERR_NOOPEN = -1,				// 设备没有open
		_IDS_RET_ERR_ALREADYOPEN = -2,			// 设备已经open
		_IDS_RET_ERR_NODEVICE = -3,				// //
		_IDS_RET_ERR_OPEN = -4,					// 打不开设备
		_IDS_RET_ERR_COMMAND = -5,				// 命令执行错误
		_IDS_RET_ERR_RECVTIMEOUT = -6,			// 接收数据超时
		_IDS_RET_ERR_RECVFAILD = -7,			// 接收数据失败
		_IDS_RET_ERR_SENDFAILD = -8,			// 发送指令失败
		_IDS_RET_COMM_ERR = -9,					// 通讯故障
		_IDS_RET_ERR_RECVSCAN_ERROR = -10,		// 接收数据失败
		_IDS_RET_ERR_SCAN_GETCARD = -11,		// 扫描证件时取卡片状态失败
		_IDS_RET_ERR_SCAN_CARD_IN_GATE = -12,	// 扫描证件时卡片在卡口，需要再次执行扫描
		_IDS_RET_ERR_SCAN_NOCARD = -13,			// 扫描证件时无卡
		_IDS_RET_ERR_IMG_INVALID = -14,			// 图片文件无效
		_IDS_RET_ERR_SAVEJPG = -15,				// 保存JPG图片失败
		_IDS_RET_ERR_SAVEPNG = -16,				// 保存PNG图片失败
		_IDS_RET_ERR_IMGNAME = -17,				// 图片名称为空
		_IDS_RET_ERR_MRZOCR = -18,				// MRZ OCR错误
		_IDS_RET_PARAM = -19,					// 错误的参数
		_IDS_RET_ERR_NOCARD = -20,	            // 卡介质不存在
		_IDS_RET_ERR_4E = -21,					// 接收数据返回4E
		_IDS_RET_ERR_MRZ = -22,					// MRZ错误
		_IDS_RET_ERR_MRZ_DGKEY = -23,			// MRZ错误
		_IDS_RET_ERR_SAVEBMP = -24,				// 保存BMP图片失败
		_IDS_RET_ERR_READ = -25,				// 读取失败
		_IDS_RET_ERR_WLT = -26,					// 身份证头像解码失败
		_IDS_RET_ERR_MEDIA = -27,               // 卡介质不存在\接收数据返回4E
		_IDS_RET_ERR_FINGER = -28,				// 读指纹失败

		_IDS_RET_HeaderERR = -96,				// 包头不是F2错误
		_IDS_RET_PackageErr = -97,				// 接收数据包命令字错误
		_IDS_RET_ERR = -98,						// 一般错误
		_IDS_RET_ERR_UNAUTHORIZED = -99,		// 未授权
		_IDS_RET_ERR_hMutex = -100,             // 互斥失败
		_IDS_RET_ERR_GET_PIC_DATA = -101,		// 获取图片数据出错

		_IDS_RET_ERR_FILENOTEXISET = -102,		// 文件不存在
		_IDS_RET_ERR_RECOGNIZEQRFAILED = -103,	// 识别二维码错误
		_IDS_RET_ERR_NOTEXISETQR = -104,		// 不存在二维码
		_IDS_RET_ERR_SAVEPIC = -105,			// 保存图片失败
		_IDS_RET_ERR_JAM = -106,				// 卡住

		_IDS_RET_ERR_UNSUPPORTED = -110,		// 不支持的功能
		_IDS_RET_ERR_SIDE = -111,				// 不支持的扫描面向类型
		_IDS_RET_ERR_MODE = -112,				// 不支持的颜色模式
		_IDS_RET_ERR_TYPE = -113,				// 不支持的卡类型
		_IDS_RET_ERR_DPI = -114,				// 不支持的分辨率
		_IDS_RET_ERR_DELAY = -115,				// 延时时间超出范围
		_IDS_RET_ERR_RF = -116,					// RF读取失败

		_IDS_RET_ERR_CPUEMV = -150,				// CPU卡片ATR信息不符合EMV方式
		_IDS_RET_ERR_CISDATA = -160,			// 像素表获取失败
		_IDS_RET_ERR_SetCISParam = -161,		// 启动扫描（拍照）失败

		_IDS_RET_ERR_LoadDllFail = -162,		// 加载dll失败


	}_IDS_RET;

	//---------------------------------------

	/// <summary>
	/// 连接crt7xxx设备
	/// </param>
	/// <param name="pcFileSavePath"		[IN]>初始化目录（扫描后的图像保存在此目录下，例如:"C:\\ProgramData\\"）</param>
	/// </summary>
	/// <returns>
	/// -2 设备已经open; -3 找不到设备; 0 打开成功
	/// </returns>
	_IDS_RET OpenDev(char* pcFileSavePath);

	/// <summary>
	/// 获取版本信息
	/// </summary>
	/// <param name="iVerType"			[IN]>1:读卡机序列号;
	///										2:固件版本号;
	///										3:获取SDK版本;
	///										4:读客户信息序列号
	/// </param>
	/// <param name="szVersionInfo"		[OUT]>识别的mrz</param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET GetVersionInfo(int iVerType, char* szVersionInfo);

	/// <summary>
	/// 断开连接crt7xxx设备
	/// </summary>
	/// <returns>
	/// 
	/// </returns>
	_IDS_RET Uninit();

	/// <summary>
	/// 发送控制指令
	/// </summary>
	/// <param name="iSendDataLen"			[IN]>指令数据长度</param>
	/// <param name="bySendData"			[IN]>指令数据，指令查阅设备通讯协议文档，格式：3.1.命令（HOST–> READER）</param>
	/// <param name="iRecvDataLen"			[OUT]>返回数据长度</param>
	/// <param name="byRecvData"			[OUT]>返回数据</param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	/// _IDS_RET  CRT7005_ExeCommand(int iSendDataLen, BYTE bySendData[] , int* iRecvDataLen, BYTE byRecvData[])
	/// 改成
	_IDS_RET CRT7005_ExeCommand(int iSendDataLen, unsigned char bySendData[], int* iRecvDataLen, unsigned char byRecvData[]);

	/// <summary>
	/// 允许、禁止进卡
	/// </summary>
	/// <param name="_shutterType"			[IN]>设备位置（前端、后端）</param>
	/// <param name="_shutterCap"			[IN]>允许、禁止</param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET CtrlShutter(EN_IDS_SHUTTER_TYPE _shutterType, EN_IDS_SHUTTER_CAP _shutterCap);

	/// <summary>
	/// 移动卡片
	/// </summary>
	/// <param name="_Pos"					[IN]>移动卡片到的位置，见_IDS_MOVE_POS</param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET Move(_IDS_MOVE_POS const _Pos);

	/// <summary>
	/// 获取卡片状态
	/// </summary>
	/// <param name="iStatus"				[OUT]>读卡器内卡片位置
	///											0:无卡，且无卡插入
	///											1:卡片在移动中
	///											2:卡片停留在前端
	///											3:卡片停留在机内卡位
	///											4:卡片停留后端
	/// </param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET CardStatus(int* iStatus);

	_IDS_RET GetStatus(_IDS_STATUS_RESULT* pResult);

	/// <summary>
	/// 扫描
	/// </summary>
	/// <param name="_ScanConf"				[IN]>扫描参数,见_IDS_SCAN_CONF</param>
	/// <param name="pResult"				[OUT]>扫描返回的参数,见_IDS_SCAN_RESULT</param>
	/// </param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET Scan(_IDS_SCAN_CONF const _ScanConf, _IDS_SCAN_RESULT* pResult);

	/// <summary>
	/// 转换bmp图片为jpg图片
	/// </summary>
	/// <param name="pcBmpFile"				[IN]>需要转换的bmp文件名，含路径</param>
	/// <param name="pcJpegFile"			[IN]>转换保存的jpg文件名，含路径</param>
	/// <param name="bDelSrc"				[IN]>转换后是否删除原来bmp文件</param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET Bmp2Jpeg(int lQuality, char* pcBmpFile, char* pcJpegFile, bool bDelSrc);

	/// <summary>
	/// 转换bmp图片为png图片
	/// </summary>
	/// <param name="pcBmpFile"				[IN]>需要转换的bmp文件名，含路径</param>
	/// <param name="pcPngFile"				[IN]>转换保存的png文件名，含路径</param>
	/// <param name="bDelSrc"				[IN]>转换后是否删除原来bmp文件</param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET Bmp2Png(int lQuality, char* pcBmpFile, char* pcPngFile, bool bDelSrc);

	/// <summary>
	/// 合成图片
	/// </summary>
	/// <param name="comPath"				[IN]>合成生成的图片文件，含路径</param>
	/// <param name="img1"					[IN]>需要合成的图片文件1</param>
	/// <param name="img2"					[IN]>需要合成的图片文件2</param>
	/// <param name="icomType"				[IN]>合成方式；0 = 横向， 1 = 纵向</param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET CRT_CombineImg(char* comPath, char* img1, char* img2, int icomType);

	/// <summary>
	/// 改变图片dpi
	/// </summary>
	/// <param name="imgPath"				[IN]>需要改变图片文件名，含路径</param>
	/// <param name="dpi"					[IN]>dpi：100,200,300,600</param>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	_IDS_RET CrtchangeImgDpi(char* imgPath, int dpi);

	/// <summary>
	/// 识别扫描的图片中的mrz
	/// </summary>
	/// <param name="pResult.cFileNameFront"	[IN]>扫描后返回的正面图片</param>
	/// <param name="pResult.cFileNameBack"	[IN]>扫描后返回的反面图片</param>
	/// <param name="mrzData"				[OUT]>识别的mrz</param>
	/// <param name="mrzLen"				[OUT]>识别的mrz数据长度</param>
	/// <param name="CardType"				[OUT]>证件类型-国家（自定义）</param>
	/// <param name="CardTypeName"			[OUT]>证件类型-国家名称</param>
	/// <returns>=0 识别成功(mrz在back);=1 识别成功(mrz在front)
	/// = -17 mrz识别错误
	/// =其他 错误
	/// </returns>
	//int MRZ_Recognition(_IDS_SCAN_RESULT pResult, char* mrzData, int* mrzLen);
	//改成
	_IDS_RET RecognizeMRZ(_IDS_SCAN_RESULT pResult, char* mrzData, int* mrzLen, int* CardType, unsigned char* CardTypeName);

	_IDS_RET ScanMRZ(_IDS_SCAN_CONF const _ScanConf, _IDS_SCAN_RESULT* pResult, unsigned char* mrzData, int* mrzLen);

	/// <summary>
	/// 设置白平衡
	/// </summary>
	/// <returns>=0 成功; =其他 错误
	/// </returns>
	int SetWhiteBalance();

	_IDS_RET FWUpgrader(char* szPath);

	int SetDeviceSN(char* szDeviceSN);

	//设置放大参数
	int SetParam(unsigned short RA, unsigned short GA, unsigned short BA, unsigned short RB, unsigned short GB, unsigned short BB, int PGAR, int PGAG);

	//执行白平衡
	int SetWhiteBalance();

	//查询放大参数
	int GetParam(unsigned short* RGB_A, unsigned short* RGB_B, int* PGAR, int* PGAG);

	int GetScanHead(char* frontPath, char* HeadPath);

	// PACE/BAC卡片读取结果结构体
	typedef struct pcsc_read_result_t {
		uint8_t* dgData;        // DG数据缓冲区指针
		size_t dgDataSize;      // DG数据缓冲区大小
	};

	int pcsc_pace_read_card(const char* can, const char* birth, const char* validity, uint8_t bDG, pcsc_read_result_t* out_result);

	void pcsc_pace_result_free(pcsc_read_result_t* result);

#ifdef __cplusplus
}
#endif
