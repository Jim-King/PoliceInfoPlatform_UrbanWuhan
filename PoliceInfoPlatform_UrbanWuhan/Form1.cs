using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.DataSourcesGDB;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Controls;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.NetworkAnalysis;
using ESRI.ArcGIS.Display;

namespace PoliceInfoPlatform_UrbanWuhan
{
    public partial class Form1 : Form
    {
        //图层数据
        public IFeatureLayer pointFeatureLayer;
        public IFeatureLayer roadFeatureLayer;
        public IFeatureLayer stationLayer;
        public IFeature pFeature;//公用Feature
        public IFeatureLayer placeFeatureLayer;
        private DataTable dt = new DataTable();
        //几何网络
        private IGeometricNetwork mGeometricNetwork;
        //网络实际顶点
        private IFeature org_stationFeature;
        private IFeature des_placeFeature; 
        //给定点的集合
        private IPointCollection mPointCollection;
        //获取给定点最近的Network元素
        private IPointToEID mPointToEID;
        private int if_pick_up;

        //返回结果变量
        private IEnumNetEID mEnumNetEID_Junctions;
        private IEnumNetEID mEnumNetEID_Edges;
        private double mdblPathCost;

        public Form1()
        {
            ESRI.ArcGIS.RuntimeManager.Bind(ESRI.ArcGIS.ProductCode.EngineOrDesktop);
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

            if_pick_up = 0;
            //获取几何网络文件路径
            //注意修改此路径为当前存储路径
            string strPath = @"..\..\AE_Data\DataBase\Wuhan_Network.mdb";
            //打开工作空间
            IWorkspaceFactory pWorkspaceFactory = new AccessWorkspaceFactory();
            IFeatureWorkspace pFeatureWorkspace = pWorkspaceFactory.OpenFromFile(strPath, 0) as IFeatureWorkspace;
            //获取要素数据集
            //注意名称的设置要与上面创建保持一致
            IFeatureDataset pFeatureDataset = pFeatureWorkspace.OpenFeatureDataset("Road_Network");

            //获取network集合
            INetworkCollection pNetWorkCollection = pFeatureDataset as INetworkCollection;
            //获取network的数量,为零时返回
            int intNetworkCount = pNetWorkCollection.GeometricNetworkCount;
            if (intNetworkCount < 1)
                return;
            //FeatureDataset可能包含多个network，我们获取指定的network
            //注意network的名称的设置要与上面创建保持一致
            mGeometricNetwork = pNetWorkCollection.get_GeometricNetworkByName("Road_Network_Net");

            //将Network中的每个要素类作为一个图层加入地图控件
            IFeatureClassContainer pFeatClsContainer = mGeometricNetwork as IFeatureClassContainer;
            //获取要素类数量，为零时返回
            int intFeatClsCount = pFeatClsContainer.ClassCount;
            if (intFeatClsCount < 1)
                return;
            IFeatureClass pFeatureClass;
            IFeatureLayer pFeatureLayer;
            for (int i = 0; i < intFeatClsCount; i++)
            {
                //获取要素类
                pFeatureClass = pFeatClsContainer.get_Class(i);
                pFeatureLayer = new FeatureLayerClass();
                pFeatureLayer.FeatureClass = pFeatureClass;
                pFeatureLayer.Name = pFeatureClass.AliasName;
                //加入地图控件
                this.axMapControl1.AddLayer((ILayer)pFeatureLayer, 0);
            }
            pointFeatureLayer = new FeatureLayerClass();
            pointFeatureLayer.FeatureClass = pFeatClsContainer.get_Class(1);
            roadFeatureLayer = new FeatureLayerClass();
            roadFeatureLayer.FeatureClass = pFeatClsContainer.get_Class(0);
            //计算snap tolerance为图层最大宽度的1/100
            //获取图层数量
            int intLayerCount = this.axMapControl1.LayerCount;
            IGeoDataset pGeoDataset;
            IEnvelope pMaxEnvelope = new EnvelopeClass();
            for (int i = 0; i < intLayerCount; i++)
            {
                //获取图层
                pFeatureLayer = this.axMapControl1.get_Layer(i) as IFeatureLayer;
                pGeoDataset = pFeatureLayer as IGeoDataset;
                //通过Union获得较大图层范围
                pMaxEnvelope.Union(pGeoDataset.Extent);
            }
            double dblWidth = pMaxEnvelope.Width;
            double dblHeight = pMaxEnvelope.Height;
            double dblSnapTol;
            if (dblHeight < dblWidth)
                dblSnapTol = dblWidth * 0.01;
            else
                dblSnapTol = dblHeight * 0.01;

            //设置源地图，几何网络以及捕捉容差
            mPointToEID = new PointToEIDClass();
            mPointToEID.SourceMap = this.axMapControl1.Map;
            mPointToEID.GeometricNetwork = mGeometricNetwork;
            mPointToEID.SnapTolerance = dblSnapTol;
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            skinEngine1.SkinFile = @"..\..\skin\皮肤\Emerald\EmeraldColor1.ssk";
        }

        private void cboPoliceStation_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = cboPoliceStation.SelectedIndex;
            string police_name = cboPoliceStation.Items[index].ToString();
            this.org_stationFeature = get_FeatureByName(this.pointFeatureLayer,police_name);
            this.axMapControl1.Map.ClearSelection();
            this.axMapControl1.Map.SelectFeature(pointFeatureLayer, this.org_stationFeature);
            this.axMapControl1.Refresh();
            LoadQueryResult(pointFeatureLayer, this.org_stationFeature);
            this.dataGridView1.DataSource = this.dt.DefaultView;
        }

        private void btn_police_search_Click(object sender, EventArgs e)
        {
            string strPath = @"..\..\AE_Data\DataBase\Wuhan_Network.mdb";
            //获取数据集的集合
            List<IFeatureClass> pFeatureClasses = new List<IFeatureClass>();
            pFeatureClasses = this.OpenMdb(strPath);
            //变量要素类集合的每个要素类
            foreach (IFeatureClass pFeatureClass in pFeatureClasses)
            {
                IFeatureLayer pFeatureLayer = new FeatureLayerClass();
                if (pFeatureClass.AliasName=="point_location")
                {
                    pFeatureLayer.FeatureClass = pFeatureClass;
                    //要素图层加入到MapControl
                    pFeatureLayer.Name = "police_station";
                    this.axMapControl1.AddLayer((ILayer)pFeatureLayer);
                    pointFeatureLayer =pFeatureLayer;
                }              
            }
            load_policeStation(pointFeatureLayer);

        }

        private void LoadQueryResult(IFeatureLayer featureLayer, IFeature pFeature)
        {
            IFeatureClass pFeatureClass = featureLayer.FeatureClass;
            dt = new DataTable();
            //根据图层属性字段初始化DataTable
            IFields pFields = pFeatureClass.Fields;
            if (this.dt.Rows.Count== 0)
            {
                for (int i = 0; i < pFields.FieldCount; i++)
                {
                    string strFldName;
                    strFldName = pFields.get_Field(i).AliasName;
                    dt.Columns.Add(strFldName);
                }
            }
            
                string strFldValue = null;
                DataRow dr = dt.NewRow();
                //遍历图层属性表字段值，并加入pDataTable
                for (int i = 0; i < pFields.FieldCount; i++)
                {
                    string strFldName = pFields.get_Field(i).Name;
                    if (strFldName == "Shape")
                    {
                        strFldValue = Convert.ToString(pFeature.Shape.GeometryType);
                    }
                    else
                        strFldValue = Convert.ToString(pFeature.get_Value(i));
                    dr[i] = strFldValue;
                }
                dt.Rows.Add(dr);
                //高亮选择要素 
        }

        public List<IFeatureClass> OpenMdb(string mdbpath)
        {
            List<IDataset> pDatasets = new List<IDataset>();
            List<IFeatureClass> pFeatureClasses = new List<IFeatureClass>();
            //定义空间工厂，打开mdb数据库
            IWorkspaceFactory pAccessFactory = new AccessWorkspaceFactoryClass();
            IWorkspace pWorkspace = pAccessFactory.OpenFromFile(mdbpath, 0);
            //获取数据集的集合
            IEnumDataset pEnumDataset = pWorkspace.get_Datasets(esriDatasetType.esriDTAny);
            pEnumDataset.Reset();
            IDataset pDataset = pEnumDataset.Next();
            string strDatasetName = "Road_Network";
            //定义要素工厂，获取要素类的集合
            IFeatureWorkspace pFeatureWorkspace = pWorkspace as IFeatureWorkspace;
            IFeatureDataset pFeatureDataset = pFeatureWorkspace.OpenFeatureDataset(strDatasetName);
            IEnumDataset pEnumDataset2 = pFeatureDataset.Subsets;
            pEnumDataset.Reset();
            IDataset pDataset2 = pEnumDataset.Next();
            //遍历要素类的集合吗，并将要素类加入要素类集合pFeatureClasses
            while (pDataset2 != null)
            {
                if (pDataset2 is IFeatureClass)
                {
                    pFeatureClasses.Add(pDataset2 as IFeatureClass);
                }
                pDataset2 = pEnumDataset2.Next();
            }
            pDatasets.Add(pDataset);
            return pFeatureClasses;
          }

        public void load_policeStation(IFeatureLayer policeLayer)
        {
            this.cboPoliceStation.Items.Clear();
            //获取cboLayer中选中的图层
            IFeatureClass pFeatureClass = policeLayer.FeatureClass;
            //字段名称
            string strFldName = pFeatureClass.Fields.get_Field(4).Name;
            for (int i = 0; i < pFeatureClass.FeatureCount(null); i++)
            {
                string temp_name;
                temp_name = pFeatureClass.GetFeature(i + 1).get_Value(4).ToString();
                //图层名称加入cboField
                this.cboPoliceStation.Items.Add(temp_name);
            }
            //默认显示第一个选项
            this.cboPoliceStation.SelectedIndex = 0;
        }

        private IFeature get_FeatureByName(IFeatureLayer pFeatureLayer,string pname)
        {
            IFeatureCursor pFeatureCursor;
            IQueryFilter pQueryFilter;
            string FieldName;

            //pQueryFilter的实例化
            pQueryFilter = new QueryFilterClass();
            //设置查询过滤条件
            FieldName = "name";
            pQueryFilter.WhereClause = FieldName + "='" + pname + "'";
            //查询
            pFeatureCursor = pFeatureLayer.Search(pQueryFilter, true);
            //获取查询到的要素
            pFeature = pFeatureCursor.NextFeature();
            return pFeature;
        }

        private void btnAddPlace_Click(object sender, EventArgs e)
        {
            string strPath = @"..\..\AE_Data\DataBase\Wuhan_Network.mdb";
            //获取数据集的集合
            List<IFeatureClass> pFeatureClasses = new List<IFeatureClass>();
            pFeatureClasses = this.OpenMdb(strPath);
            //变量要素类集合的每个要素类
            foreach (IFeatureClass pFeatureClass in pFeatureClasses)
            {
                IFeatureLayer pFeatureLayer = new FeatureLayerClass();
                if (pFeatureClass.AliasName == "arrive_location")
                {
                    pFeatureLayer.FeatureClass = pFeatureClass;
                    //要素图层加入到MapControl
                    pFeatureLayer.Name = "domicile";
                    this.axMapControl1.AddLayer((ILayer)pFeatureLayer);
                    placeFeatureLayer = pFeatureLayer;
                }
            }
        }

        private void AddDestination(IFeature pFeature)
        {
            this.treeView1.BeginUpdate();
            TreeNode node = this.treeView1.Nodes.Add("出警目标地址");
            double xcoord;
            double ycoord;
            xcoord = pFeature.Shape.Envelope.XMax;
            ycoord = pFeature.Shape.Envelope.YMax;
            node.Nodes.Add("X="+xcoord.ToString());
            node.Nodes.Add("Y="+ycoord.ToString());         
            node.Nodes.Add("地址："+ pFeature.get_Value(2).ToString());
            this.treeView1.EndUpdate();
        }

        private void AddStation(IFeature pFeature)
        {
            this.treeView1.BeginUpdate();
            TreeNode node = this.treeView1.Nodes.Add("出警派出所地址");
            double xcoord;
            double ycoord;
            xcoord = pFeature.Shape.Envelope.XMax;
            ycoord = pFeature.Shape.Envelope.YMax;
            node.Nodes.Add("X=" + xcoord.ToString());
            node.Nodes.Add("Y=" + ycoord.ToString());
            node.Nodes.Add("地址：" + pFeature.get_Value(4).ToString());
            this.treeView1.EndUpdate();
        }

        private void txtDestination_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode==Keys.Enter)
            {
                string des_Name=txtDestination.Text.ToString();
                this.des_placeFeature=get_FeatureByName(this.placeFeatureLayer,des_Name);
                AddDestination(this.des_placeFeature);
                this.axMapControl1.Map.ClearSelection();
                this.axMapControl1.Map.SelectFeature(placeFeatureLayer, this.des_placeFeature);
                this.axMapControl1.Refresh();
            }
        }

        private void btn_CreatePath_Click(object sender, EventArgs e)
        {
            try
            {
                AddStation(this.org_stationFeature);
                //路径计算
                //注意权重名称与设置保持一致
                PlaceToPoint();
                SolvePath("Length");
                //路径转换为几何要素
                IPolyline pPolyLineResult = PathToPolyLine();
                dataGridView1.DataSource = LoadRoadResult(roadFeatureLayer, pPolyLineResult).DefaultView;
                RoadInfo(pPolyLineResult);
                //获取屏幕显示
                IActiveView pActiveView = this.axMapControl1.ActiveView;
                IScreenDisplay pScreenDisplay = pActiveView.ScreenDisplay;
                //设置显示符号
                ILineSymbol pLineSymbol = new CartographicLineSymbolClass();
                IRgbColor pColor = new RgbColorClass();
                pColor.Red = 200;
                pColor.Green =205;
                pColor.Blue = 0;
                //设置线宽
                pLineSymbol.Width = 5;
                //设置颜色
                pLineSymbol.Color = pColor as IColor;
                //绘制线型符号
                pScreenDisplay.StartDrawing(0, 0);
                pScreenDisplay.SetSymbol((ISymbol)pLineSymbol);
                pScreenDisplay.DrawPolyline(pPolyLineResult);
                pScreenDisplay.FinishDrawing();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("路径分析出现错误:" + "\r\n" + ex.Message);
            }
            //点集设为空
            mPointCollection = null;
        }

       private void PlaceToPoint()
        {
            //记录鼠标点击的点
            IPoint pNewPoint_org = new PointClass();
            double org_mapX=this.org_stationFeature.Shape.Envelope.XMax;
            double org_mapY=this.org_stationFeature.Shape.Envelope.YMax;
            pNewPoint_org.PutCoords(org_mapX, org_mapY);
            IFeature nearFea = GetNearestFeature(pNewPoint_org, 1.2);
            IPoint pNewPoint_org2 = GetNearestPoint(pNewPoint_org, nearFea);
        

            IPoint pNewPoint_des = new PointClass();
            double des_mapX = this.des_placeFeature.Shape.Envelope.XMax;
            double des_mapY = this.des_placeFeature.Shape.Envelope.YMax;
            pNewPoint_des.PutCoords(des_mapX, des_mapY);
            nearFea = GetNearestFeature(pNewPoint_des, 1.2);
            IPoint pNewPoint_des2 = GetNearestPoint(pNewPoint_des, nearFea);
            if (mPointCollection == null)
                mPointCollection = new MultipointClass();
            //添加点，before和after标记添加点的索引，这里不定义
            object before = Type.Missing;
            object after = Type.Missing;
            mPointCollection.AddPoint(pNewPoint_org2, ref before, ref after);
            mPointCollection.AddPoint(pNewPoint_des2, ref before, ref after);
        }

       private void SolvePath(string weightName)
        {
            //创建ITraceFlowSolverGEN
            ITraceFlowSolverGEN pTraceFlowSolverGEN = new TraceFlowSolverClass();
            INetSolver pNetSolver = pTraceFlowSolverGEN as INetSolver;
            //初始化用于路径计算的Network
            INetwork pNetWork = mGeometricNetwork.Network;
            pNetSolver.SourceNetwork = pNetWork;

            //获取分析经过的点的个数
            int intCount = mPointCollection.PointCount;
            if (intCount < 1)
                return;


            INetFlag pNetFlag;
            //用于存储路径计算得到的边
            IEdgeFlag[] pEdgeFlags = new IEdgeFlag[intCount];


            IPoint pEdgePoint = new PointClass();
            int intEdgeEID;
            IPoint pFoundEdgePoint;
            double dblEdgePercent;

            //用于获取几何网络元素的UserID, UserClassID,UserSubID
            INetElements pNetElements = pNetWork as INetElements;
            int intEdgeUserClassID;
            int intEdgeUserID;
            int intEdgeUserSubID;
            for (int i = 0; i < intCount; i++)
            {
                pNetFlag = new EdgeFlagClass();
                //获取用户点击点
                pEdgePoint = mPointCollection.get_Point(i);
                //获取距离用户点击点最近的边
                mPointToEID.GetNearestEdge(pEdgePoint, out intEdgeEID, out pFoundEdgePoint, out dblEdgePercent);
                if (intEdgeEID <= 0)
                    continue;
                //根据得到的边查询对应的几何网络中的元素UserID, UserClassID,UserSubID
                pNetElements.QueryIDs(intEdgeEID, esriElementType.esriETEdge,
                    out intEdgeUserClassID, out intEdgeUserID, out intEdgeUserSubID);
                if (intEdgeUserClassID <= 0 || intEdgeUserID <= 0)
                    continue;

                pNetFlag.UserClassID = intEdgeUserClassID;
                pNetFlag.UserID = intEdgeUserID;
                pNetFlag.UserSubID = intEdgeUserSubID;
                pEdgeFlags[i] = pNetFlag as IEdgeFlag;
            }
            //设置路径求解的边
            pTraceFlowSolverGEN.PutEdgeOrigins(ref pEdgeFlags);

            //路径计算权重
            INetSchema pNetSchema = pNetWork as INetSchema;
            INetWeight pNetWeight = pNetSchema.get_WeightByName(weightName);
            if (pNetWeight == null)
                return;

            //设置权重，这里双向的权重设为一致
            INetSolverWeights pNetSolverWeights = pTraceFlowSolverGEN as INetSolverWeights;
            pNetSolverWeights.ToFromEdgeWeight = pNetWeight;
            pNetSolverWeights.FromToEdgeWeight = pNetWeight;

            object[] arrResults = new object[intCount - 1];
            //执行路径计算
            pTraceFlowSolverGEN.FindPath(esriFlowMethod.esriFMConnected, esriShortestPathObjFn.esriSPObjFnMinSum,
                out mEnumNetEID_Junctions, out mEnumNetEID_Edges, intCount - 1, ref arrResults);

            //获取路径计算总代价（cost）
            mdblPathCost = 0;
            for (int i = 0; i < intCount - 1; i++)
                mdblPathCost += (double)arrResults[i];
        }
       private IPolyline PathToPolyLine()
       {
           IPolyline pPolyLine = new PolylineClass();
           IGeometryCollection pNewGeometryCollection = pPolyLine as IGeometryCollection;
           if (mEnumNetEID_Edges == null)
               return null;

           IEIDHelper pEIDHelper = new EIDHelperClass();
           //获取几何网络
           pEIDHelper.GeometricNetwork = mGeometricNetwork;
           //获取地图空间参考
           ISpatialReference pSpatialReference = this.axMapControl1.Map.SpatialReference;
           pEIDHelper.OutputSpatialReference = pSpatialReference;
           pEIDHelper.ReturnGeometries = true;
           //根据边的ID获取边的信息
           IEnumEIDInfo pEnumEIDInfo = pEIDHelper.CreateEnumEIDInfo(mEnumNetEID_Edges);
           int intCount = pEnumEIDInfo.Count;
           pEnumEIDInfo.Reset();

           IEIDInfo pEIDInfo;
           IGeometry pGeometry;
           for (int i = 0; i < intCount; i++)
           {
               pEIDInfo = pEnumEIDInfo.Next();
               //获取边的几何要素
               pGeometry = pEIDInfo.Geometry;
               pNewGeometryCollection.AddGeometryCollection((IGeometryCollection)pGeometry);
           }
           return pPolyLine;
       }
       private DataTable LoadRoadResult(IFeatureLayer featureLayer, IGeometry geometry)
       {
           IFeatureClass pFeatureClass = featureLayer.FeatureClass;

           //根据图层属性字段初始化DataTable
           IFields pFields = pFeatureClass.Fields;
           DataTable pDataTable = new DataTable();
           for (int i = 0; i < pFields.FieldCount; i++)
           {
               string strFldName;
               strFldName = pFields.get_Field(i).AliasName;
               pDataTable.Columns.Add(strFldName);
           }

           //空间过滤器
           ISpatialFilter pSpatialFilter = new SpatialFilterClass();
           pSpatialFilter.Geometry = geometry;

           //根据图层类型选择缓冲方式
           switch (pFeatureClass.ShapeType)
           {
               case esriGeometryType.esriGeometryPoint:
                   pSpatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelContains;
                   break;
               case esriGeometryType.esriGeometryPolyline:
                   pSpatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelOverlaps;
                   break;
               case esriGeometryType.esriGeometryPolygon:
                   pSpatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelIntersects;
                   break;
           }
           //定义空间过滤器的空间字段
           pSpatialFilter.GeometryField = pFeatureClass.ShapeFieldName;

           IQueryFilter pQueryFilter;
           IFeatureCursor pFeatureCursor;
           IFeature pFeature;
           //利用要素过滤器查询要素
           pQueryFilter = pSpatialFilter as IQueryFilter;
           pFeatureCursor = featureLayer.Search(pQueryFilter, true);
           pFeature = pFeatureCursor.NextFeature();

           while (pFeature != null)
           {
               string strFldValue = null;
               DataRow dr = pDataTable.NewRow();
               //遍历图层属性表字段值，并加入pDataTable
               for (int i = 0; i < pFields.FieldCount; i++)
               {
                   string strFldName = pFields.get_Field(i).Name;
                   if (strFldName == "Shape")
                   {
                       strFldValue = Convert.ToString(pFeature.Shape.GeometryType);
                   }
                   else
                       strFldValue = Convert.ToString(pFeature.get_Value(i));
                   dr[i] = strFldValue;
               }
               pDataTable.Rows.Add(dr);
               //高亮选择要素
               pFeature = pFeatureCursor.NextFeature();
           }
           return pDataTable;
       }
       private void RoadInfo(IPolyline pPolylineResult)
       {
           double road_result = pPolylineResult.Length*100;
           this.treeView1.BeginUpdate();
           TreeNode node = this.treeView1.Nodes.Add("出警路线信息");
           node.Nodes.Add("长度(km)：" + road_result.ToString());
           node.Nodes.Add(("时间(min)：" + (road_result*2)).ToString());
           this.treeView1.EndUpdate();
       }
       ////得到指定图层上距point最近的feature上的最近点
       public IPoint GetNearestPoint(IPoint point, IFeature nearFea)
        {
            IProximityOperator Proximity = (IProximityOperator)point;
            IFeatureLayer FeaLyr = roadFeatureLayer;
            IFeatureClass FeaCls = FeaLyr.FeatureClass;
            IQueryFilter queryFilter = null;
            ITopologicalOperator topoOper = (ITopologicalOperator)point;
            IGeometry geo = topoOper.Buffer(1.2);
            ISpatialFilter sf = new SpatialFilter();
            sf.Geometry = geo;
            sf.GeometryField = FeaCls.ShapeFieldName;
            sf.SpatialRel = esriSpatialRelEnum.esriSpatialRelCrosses;
            IFeatureCursor FeaCur = FeaCls.Search(queryFilter, false);
            IFeature Fea = nearFea = FeaCur.NextFeature();
            double minDistince, Distance;
            if (Fea == null)
                return null;
            minDistince = Distance = Proximity.ReturnDistance((IGeometry)Fea.Shape);    //最近的距离值
            //保存距离最近的feature
            Fea = FeaCur.NextFeature();
            while (Fea != null)
            {
                Distance = Proximity.ReturnDistance((IGeometry)Fea.Shape);
                if (Distance < minDistince)
                {
                    minDistince = Distance;
                    nearFea = Fea;
                }
                Fea = FeaCur.NextFeature();
            }   //end while
            Proximity = (IProximityOperator)nearFea.Shape;
            return Proximity.ReturnNearestPoint(point, esriSegmentExtension.esriNoExtension);
        }

       public IFeature GetNearestFeature(IPoint p, double rongcha)
       {
           IFeature nearFea;
           IProximityOperator Proximity = (IProximityOperator)p;
           IFeatureLayer FeaLyr = roadFeatureLayer;
           IFeatureClass FeaCls = FeaLyr.FeatureClass;
           IQueryFilter queryFilter = null;

           IFeatureCursor FeaCur = FeaCls.Search(queryFilter, false);
           IFeature Fea = nearFea = FeaCur.NextFeature();

           double minDistince, Distance;
           if (Fea == null)
               return null;

           Distance =100;    //最近的距离值
           minDistince = Distance;
           //保存距离最近的feature
           Fea = FeaCur.NextFeature();
           while (Fea != null)
           {
               Distance = Proximity.ReturnDistance((IGeometry)Fea.Shape);
               if (Distance < minDistince)
               {
                   minDistince = Distance;
                   nearFea = Fea;
               }
               Fea = FeaCur.NextFeature();
           }   //end while
           return nearFea;
       }

    }
}


