﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;

namespace GMapDownloader
{
    public class RouteMapData
    {
        public string Datetime { set; get; }

        [DisplayName("经度")]
        public double Lng { set; get; }

        [DisplayName("纬度")]
        public double Lat { set; get; }

        //信号强度key value
        public Dictionary<string, double> SignalDataDictionary { set; get; }

        public RouteMapData()
        {

        }

        public RouteMapData(double lat, double lng, Dictionary<string, double> signalDataDictionary)
        {
            this.Lat = lat;
            this.Lng = lng;
            this.SignalDataDictionary = signalDataDictionary;
        }
    }
}
