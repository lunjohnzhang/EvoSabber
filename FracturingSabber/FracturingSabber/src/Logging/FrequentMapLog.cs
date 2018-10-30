using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

using FracturingSabber.Mapping;
using FracturingSabber.Search;

namespace FracturingSabber.Logging
{
	// A compressed feature map for frequence logging.
   // Doesn't contain detailed individual information.
   class FrequentMapLog
   {
		private string _logPath;
		private FeatureMap _map;

      public FrequentMapLog(string logPath, FeatureMap map)
      {
         _logPath = logPath;
         _map = map;
         
         // Create a log for individuals
         using (FileStream ow = File.Open(logPath,
					 FileMode.Create, FileAccess.Write, FileShare.None))
         {
            string[] dataLabels = {
                  "Dimensions",
                  "Map (f1:f2:Size:Wins:Fitness:Featur1:Feature2)"
               };

            WriteText(ow, string.Join(",", dataLabels));
            ow.Close();
         }
      }

      private static void WriteText(Stream fs, string s)
      {
         s += "\n";
         byte[] info = new UTF8Encoding(true).GetBytes(s);
         fs.Write(info, 0, info.Length);
      }

      // Call this whenever you want the log to update with the latest
      // feature map data.
      public void UpdateLog()
      {
         using (StreamWriter sw = File.AppendText(_logPath))
         {
            var rowData = new List<string>();
            IEnumerable<int> dimensions = 
               Enumerable.Repeat(_map.NumGroups, _map.NumFeatures);
        
            rowData.Add(string.Join("x", dimensions));

            foreach (string index in _map.EliteMap.Keys)
            {
               List<Individual> elems = _map.EliteMap[index];
               Individual cur = elems[0];
               var cellComponents = new List<string>();
               cellComponents.Add(index);
               cellComponents.Add(elems.Count.ToString());
               cellComponents.Add(cur.WinCount.ToString());
               cellComponents.Add(cur.RawFitness.ToString());
               cellComponents.Add(cur.Features[0].ToString());
               cellComponents.Add(cur.Features[1].ToString());
               
               rowData.Add(string.Join(":", cellComponents));
            }

            sw.WriteLine(string.Join(",", rowData));
         }
      }
   }
}
