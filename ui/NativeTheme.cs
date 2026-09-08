using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace Wintools {
    internal static class NativeTheme {
        internal static bool IsDark(string mode){if(mode=="dark")return true;if(mode=="light")return false;try{using(var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))return key!=null&&Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;}catch{return false;}}
        internal static Dictionary<string,string> Colors(bool dark) {
            string[] keys={"Background","Sidebar","Surface","Raised","Border","Text","Muted","Accent","AccentText","Selection","Danger"};
            string[] values=dark?new[]{"#101114","#15161B","#1C1E24","#252830","#373B46","#F5F6FA","#B2B7C4","#85ADFF","#101C38","#263653","#FFB4AD"}:new[]{"#F5F6FA","#ECEEF4","#FFFFFF","#F1F3F8","#D4D9E4","#1A2030","#586176","#285BD7","#FFFFFF","#E5EDFF","#AE302B"};
            if(SystemParameters.HighContrast)values=new[]{SystemColors.WindowColor.ToString(),SystemColors.WindowColor.ToString(),SystemColors.WindowColor.ToString(),SystemColors.ControlColor.ToString(),SystemColors.WindowTextColor.ToString(),SystemColors.WindowTextColor.ToString(),SystemColors.WindowTextColor.ToString(),SystemColors.HighlightColor.ToString(),SystemColors.HighlightTextColor.ToString(),SystemColors.ControlColor.ToString(),SystemColors.WindowTextColor.ToString()};
            var result=new Dictionary<string,string>();for(int i=0;i<keys.Length;i++)result.Add(keys[i],values[i]);return result;
        }
        [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        internal static void TitleBar(IntPtr handle,bool dark){try{int value=dark?1:0;DwmSetWindowAttribute(handle,20,ref value,sizeof(int));}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
    }
}
