﻿using System;
using System.Globalization;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Sdl.Web.Mvc.Models;

namespace Sdl.Web.Mvc.Html
{
    public static class HtmlHelperExtensions
    {
        /*public static string ResponsiveImageUrl(this HtmlHelper helper, string url, int baseSize, bool fixHeight = false)
        {
            double maxWidth = WebRequestContext.MaxMediaWidth;
            int factor = (int)Math.Ceiling(maxWidth / baseSize);
            factor = factor > 4 ? 8 : factor==3 ? 4 : factor;//factor is 1x 2x 4x or 8x our base (small screen) width - as we only want to support 4 versions of an image, and want to cap it at base x 8
            factor = WebRequestContext.ContextEngine.Device.PixelRatio == 1 && factor > 4 ? 4 : factor;//max x4 for pixel ratio of 1 
            int width = factor * baseSize;
            return String.Format("/cid/fit/{0}{1}/source/site{2}", fixHeight ? "x" : "", width, url);
        }

        public static string FixedSizeImageUrl(this HtmlHelper helper, string url, int size, bool fixHeight = true, double aspect = 1.62)
        {
            size = size * WebRequestContext.ContextEngine.Device.PixelRatio;
            int height = fixHeight ? size : (int) (size / aspect);
            int width = fixHeight ? (int)(size * aspect) : size;
            return String.Format("/cid/scale/{0}x{1}/source/site{2}", width, height, url);
         * 
        }*/

        public static string Date(this HtmlHelper htmlHelper, DateTime? date, string format = "D")
        {
            return date != null ? ((DateTime)date).ToString(format, new CultureInfo(Configuration.GetConfig("core.culture"))) : null;
        }

        public static string DateDiff(this HtmlHelper htmlHelper, DateTime? date, string format = "D")
        {
            //TODO make the text come from resources
            if (date!=null)
            {
                int dayDiff = (int)(DateTime.Now.Date - ((DateTime)date).Date).TotalDays;
                if (dayDiff <= 0)
                {
                    return htmlHelper.Resource("core.todayText");
                }
                if (dayDiff == 1)
                {
                    return htmlHelper.Resource("core.yesterdayText");
                }
                if (dayDiff <= 7)
                {
                    return String.Format(htmlHelper.Resource("core.xDaysAgoText"), dayDiff);
                }
                else
                {
                    return ((DateTime)date).ToString(format, new CultureInfo(Configuration.GetConfig("core.culture")));
                }
            }
            return null;
        }

        public static string FormatResource(this HtmlHelper htmlHelper, string resourceName, params object[] parameters)
        {
            return String.Format((string)htmlHelper.Resource(resourceName),parameters);
        }

        public static string Resource(this HtmlHelper htmlHelper, string resourceName)
        {
            return (string)Resource(htmlHelper.ViewContext.HttpContext, resourceName);
        }

        public static object FormatResource(this HttpContextBase httpContext, string resourceName, params object[] parameters)
        {
            return String.Format((string)httpContext.Resource(resourceName), parameters);
        }

        public static object Resource(this HttpContextBase httpContext, string resourceName)
        {
            return httpContext.GetGlobalResourceObject(CultureInfo.CurrentUICulture.ToString(), resourceName);
        }

    }
}