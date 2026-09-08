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
            string[] values=dark?new[]{"#101415","#14191B","#1B2224","#252E30","#303B3D","#F0F5F3","#9BAEAA","#B4E0C8","#132C21","#293F36","#F3B6A5"}:new[]{"#F4F6F3","#EDEFEB","#FFFFFF","#EDF1EC","#D9E1D9","#1A2A24","#62736B","#245B43","#FFFFFF","#DCEBDF","#A53E2D"};
            if(SystemParameters.HighContrast)values=new[]{SystemColors.WindowColor.ToString(),SystemColors.WindowColor.ToString(),SystemColors.WindowColor.ToString(),SystemColors.ControlColor.ToString(),SystemColors.WindowTextColor.ToString(),SystemColors.WindowTextColor.ToString(),SystemColors.WindowTextColor.ToString(),SystemColors.HighlightColor.ToString(),SystemColors.HighlightTextColor.ToString(),SystemColors.ControlColor.ToString(),SystemColors.WindowTextColor.ToString()};
            var result=new Dictionary<string,string>();for(int i=0;i<keys.Length;i++)result.Add(keys[i],values[i]);return result;
        }
        [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        internal static void TitleBar(IntPtr handle,bool dark){try{int value=dark?1:0;DwmSetWindowAttribute(handle,20,ref value,sizeof(int));}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
    }
}
