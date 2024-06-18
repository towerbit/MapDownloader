### 2024-06-18 更新说明
* 修正 绘图时报错的问题
* 修正 腾讯地图/船舶地图无显示的问题
* 修正 必应中文地图显示为英文地图的问题
* 增加 选择文件切片时，允许选择存储目录
* 增加 支持自定义下载瓦片文件命名输出格式


# MapDownloader
Map downloader based on [GMap.NET](https://github.com/radioman/greatmaps)

中文详情：[Chinese Details](https://www.cnblogs.com/luxiaoxun/p/4454880.html)
### Features:
* Support maps from Baidu(百度), Amap(高德), Tencent(腾讯), Tianditu(天地图), Ship, Google, Bing, OpenStreetMap, ArcGIS, Here(Nokia)...
* Store maps into SQLite(Data.gmdb) and MYSQL.
* Search POI(Points of Interest) and export into excel.
* Show maps based on Chinese boundary.
* Draw line, polygon, circle and marker.
* Show distance between 2 points.
* Show postion based on keywords.
* Web GIS(work with Leaflet).
### GUI:
![GUI](https://github.com/luxiaoxun/MapDownloader/blob/master/Info/GUI.png)

### API KEY
PS： Please use your own KEY from Biadu, Amap and Tencent to search POIs! 
* http://lbsyun.baidu.com/index.php?title=webapi
* https://lbs.amap.com/api/webservice/summary/
* https://lbs.qq.com/webservice_v1/index.html

声明：本软件仅供个人学习与科研使用，所下载的数据版权归各个地图服务商所有，任何组织或个人因数据使用不当造成的问题，软件作者不负责任。
