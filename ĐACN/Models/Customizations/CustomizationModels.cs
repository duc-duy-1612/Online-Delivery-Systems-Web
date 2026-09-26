using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Newtonsoft.Json;

namespace DACN.Models.Customizations
{
    public class CustomizationOption
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
    }

    public class CustomizationGroup
    {
        public string GroupName { get; set; }
        public bool IsRequired { get; set; }
        public bool IsMultiple { get; set; }
        public List<CustomizationOption> Options { get; set; } = new List<CustomizationOption>();
    }

    public class CustomizationService
    {
        private static string GetFilePath()
        {
            var folder = HttpContext.Current.Server.MapPath("~/App_Data");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, "Customizations.json");
        }

        public static Dictionary<string, List<CustomizationGroup>> GetAllCustomizations()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
            {
                return new Dictionary<string, List<CustomizationGroup>>();
            }
            string json = File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<Dictionary<string, List<CustomizationGroup>>>(json);
            return data ?? new Dictionary<string, List<CustomizationGroup>>();
        }

        public static List<CustomizationGroup> GetCustomizationForDish(string maMon)
        {
            var all = GetAllCustomizations();
            if (all.ContainsKey(maMon))
            {
                return all[maMon];
            }
            return new List<CustomizationGroup>();
        }

        public static void SaveCustomizationForDish(string maMon, List<CustomizationGroup> groups)
        {
            var all = GetAllCustomizations();
            all[maMon] = groups;
            string json = JsonConvert.SerializeObject(all, Formatting.Indented);
            File.WriteAllText(GetFilePath(), json);
        }
    }
}
