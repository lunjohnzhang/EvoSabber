using System;
using System.Collections.Generic;
using System.IO;

using MapSabber.Config;
using MapSabber.Mapping.Sizers;
using MapSabber.Search;

/* This is a FeatureMap that has fixed boundaries at even intervals.
 * It is exactly as described in the original MAP-Elites paper.
 */

namespace MapSabber.Mapping
{
   class FixedFeatureMap : FeatureMap
   {
      private static Random rnd = new Random();
      private List<Individual> _allIndividuals; 

      private MapSizer _groupSizer; 
      private int _maxIndividualsToEvaluate;

      public int NumGroups { get; private set; }
      public int NumFeatures { get; private set; }
      public Dictionary<string, Individual> EliteMap { get; private set; }
      public Dictionary<string, int> CellCount { get; private set; }

      private List<string> _eliteIndices;
      private int[] _lowGroupBound;
      private int[] _highGroupBound;

      public FixedFeatureMap(Configuration config, MapSizer groupSizer)
      {
         _allIndividuals = new List<Individual>();
         _groupSizer = groupSizer;
         _maxIndividualsToEvaluate = config.Search.NumToEvaluate;
         NumGroups = -1;

         NumFeatures = config.Map.Features.Length;
         _lowGroupBound = new int[NumFeatures];
         _highGroupBound = new int[NumFeatures];
         for (int i=0; i<NumFeatures; i++)
         {
            _lowGroupBound[i] = config.Map.Features[i].MinValue;
            _highGroupBound[i] = config.Map.Features[i].MaxValue;
         }
      }

      private int GetFeatureIndex(int featureId, int feature)
      {
         if (feature <= _lowGroupBound[featureId])
            return 0;
         if (_highGroupBound[featureId] <= feature)
            return NumGroups-1;

         int gap = _highGroupBound[featureId] - _lowGroupBound[featureId] + 1;
         int pos = feature - _lowGroupBound[featureId];
         int index = NumGroups * pos / gap;
         return index;
      }
  
      private void AddToMap(Individual toAdd)
      {
         var features = new int[NumFeatures];
         for (int i=0; i<NumFeatures; i++)
            features[i] = GetFeatureIndex(i, toAdd.Features[i]);
         string index = string.Join(":", features);
     
         if (!EliteMap.ContainsKey(index))
         {
            _eliteIndices.Add(index);
            EliteMap.Add(index, toAdd);
            CellCount.Add(index, 0);
         }
         else if (EliteMap[index].Fitness < toAdd.Fitness)
         {
            EliteMap[index] = toAdd;
         }

         CellCount[index] += 1;
      }

      private void Remap(int nextNumGroups)
      {
         // Update the new group size
         NumGroups = nextNumGroups;

         // Repopulate the map
         _eliteIndices = new List<string>();
         EliteMap = new Dictionary<string,Individual>();
         CellCount = new Dictionary<string,int>();
         foreach (Individual cur in _allIndividuals)
            AddToMap(cur);
      }

      public void Add(Individual toAdd)
      {
         _allIndividuals.Add(toAdd);

         double portionDone = 
            1.0 * _allIndividuals.Count / _maxIndividualsToEvaluate;
         int nextNumGroups = _groupSizer.GetSize(portionDone);
         if (nextNumGroups != NumGroups)
            Remap(nextNumGroups);
         else
            AddToMap(toAdd);
      }

      public Individual GetRandomElite()
      {
         int pos = rnd.Next(_eliteIndices.Count);
         string index = _eliteIndices[pos];
         return EliteMap[index];
      }
   }
}
