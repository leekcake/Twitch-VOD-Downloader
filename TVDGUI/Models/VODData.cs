using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TVDGUI.Models
{
    public class VODData
    {
        public string StreamerId { get; set; }
        public string BroadcastDate { get; set; }
        public string BroadcastTitle { get; set; }
        public string ThumbnailURL { get; set; }

        public string VODHeader
        {
            get
            {
                var split = ThumbnailURL.Replace("//", "/").Split('/');
                return split[split.Length - 3];
            }
        }

        public string OutputFilename
        {
            get
            {
                return $"Fin_{StreamerId}_" + Regex.Replace(BroadcastDate, "[^0-9.]", "") + $"-TVD-{VODHeader}.mp4";
            }
        }

        public string Summary
        {
            get
            {
                return $"{BroadcastTitle}({StreamerId}) - {BroadcastDate}";
            }
        }

        public bool DownloadIt { get; set; }
    }
}
