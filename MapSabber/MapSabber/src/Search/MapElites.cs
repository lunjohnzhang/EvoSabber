using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using Nett;

using SabberStoneCore.Enums;
using SabberStoneCore.Model;

using MapSabber.Config;
using MapSabber.Logging;
using MapSabber.Mapping;
using MapSabber.Mapping.Sizers;
using MapSabber.Messaging;

namespace MapSabber.Search
{
   class MapElites
   {
      readonly private CardClass _heroClass;
      readonly private List<Card> _cardSet;
      private Queue<int> _runningWorkers;
      private Queue<int> _idleWorkers;

      private int _individualsEvaluated;
      private int _individualsDispatched;

      string[] featureNames;
      FeatureMap _featureMap;
      Dictionary<int, Individual> _individualStable;

      // MapElites Parameters
      private string _configFilename;
      private SearchParams _params;

      // Logging 
      private const string LOG_DIRECTORY = "logs/";
      private const string INDIVIDUAL_LOG_FILENAME = 
         LOG_DIRECTORY + "individual_log.csv";
      private const string CHAMPION_LOG_FILENAME = 
         LOG_DIRECTORY + "champion_log.csv";
      private const string FITTEST_LOG_FILENAME = 
         LOG_DIRECTORY + "fittest_log.csv";
      private const string ELITE_MAP_FILENAME = 
         LOG_DIRECTORY + "elite_map_log.csv";
      private FrequentMapLog _map_log;
      private RunningIndividualLog _individualLog;
      private RunningIndividualLog _championLog;
      private RunningIndividualLog _fittestLog;

      public MapElites(string configFilename)
      {
         // Grab the configuration info
         _configFilename = configFilename;
         var config = Toml.ReadFile<Configuration>(_configFilename);
         _params = config.Search;
   
         Console.WriteLine("NumFeatures: "+config.Map.Features.Length);
         foreach (var p in config.Map.Features)
         {
            Console.WriteLine(p.Name);
         }

         // Configuration for the search space
         _heroClass = CardReader.GetClassFromName(config.Deckspace.HeroClass);
         CardSet[] sets = CardReader.GetSetsFromNames(config.Deckspace.CardSets);
         _cardSet = CardReader.GetCards(_heroClass, sets);

         InitLogs();
         InitMap(config);
      }

      private void InitLogs()
      {
         _individualLog =
            new RunningIndividualLog(INDIVIDUAL_LOG_FILENAME);
         _championLog =
            new RunningIndividualLog(CHAMPION_LOG_FILENAME);
         _fittestLog =
            new RunningIndividualLog(FITTEST_LOG_FILENAME);
      }

      private void InitMap(Configuration config)
      {
         var mapSizer = new LinearMapSizer(config.Map.StartSize,
                                           config.Map.EndSize);
         if (config.Map.Type.Equals("SlidingFeature"))
            _featureMap = new SlidingFeatureMap(config, mapSizer);
         else if (config.Map.Type.Equals("FixedFeature"))
            _featureMap = new FixedFeatureMap(config, mapSizer);
         else
            Console.WriteLine("ERROR: No feature map specified in config file.");
        
         featureNames = new string[config.Map.Features.Length]; 
         for (int i=0; i<config.Map.Features.Length; i++)
            featureNames[i] = config.Map.Features[i].Name;

         _map_log = new FrequentMapLog(ELITE_MAP_FILENAME, _featureMap);
         _individualStable = new Dictionary<int,Individual>();
      }
         
      private static void WriteText(Stream fs, string s)
      {
         s += "\n";
         byte[] info = new UTF8Encoding(true).GetBytes(s);
         fs.Write(info, 0, info.Length);
      }

      private void SendWork(string workerInboxPath, Individual cur)
      {
         var deckParams = new DeckParams();
         deckParams.ClassName = _heroClass.ToString().ToLower();
         deckParams.CardList = cur.GetCards();

         var msg = new PlayMatchesMessage();
         msg.Deck = deckParams;

         Toml.WriteFile<PlayMatchesMessage>(msg, workerInboxPath);
      }

      private int _maxWins;
      private int _maxFitness;
      private void ReceiveResults(string workerOutboxPath, Individual cur)
      {
         // Read the message and then delete the file.
         var results = Toml.ReadFile<ResultsMessage>(workerOutboxPath);
         File.Delete(workerOutboxPath);

         // Save the statistics for this individual.
         cur.ID = _individualsEvaluated;
         cur.OverallData = results.OverallStats; 
         cur.StrategyData = results.StrategyStats; 

         // Save which elements are relevant to the search
         cur.Features = new int[featureNames.Length];
         for (int i=0; i<featureNames.Length; i++)
            cur.Features[i] = cur.GetStatByName(featureNames[i]);
         cur.Fitness = cur.OverallData.TotalHealthDifference;

         var os = results.OverallStats;
         Console.WriteLine("------------------");
         Console.WriteLine(string.Format("Eval ({0}): {1}",
               _individualsEvaluated,
               string.Join("", cur.ToString())));
         Console.WriteLine("Win Count: "+os.WinCount);
         Console.WriteLine("Total Health Difference: "
                           +os.TotalHealthDifference);
         Console.WriteLine("Damage Done: "+os.DamageDone);
         Console.WriteLine("Num Turns: "+os.NumTurns);
         Console.WriteLine("Cards Drawn: "+os.CardsDrawn);
         Console.WriteLine("Hand Size: "+os.HandSize);
         Console.WriteLine("Mana Spent: "+os.ManaSpent);
         Console.WriteLine("Mana Wasted: "+os.ManaWasted);
         Console.WriteLine("Strategy Alignment: "+os.StrategyAlignment);
         Console.WriteLine("Dust: "+os.Dust);
         Console.WriteLine("Deck Mana Sum: "+os.DeckManaSum);
         Console.WriteLine("Deck Mana Variance: "+os.DeckManaVariance);
         Console.WriteLine("Num Minion Cards: "+os.NumMinionCards);
         Console.WriteLine("Num Spell Cards: "+os.NumSpellCards);
         Console.WriteLine("------------------");
         foreach (var fs in results.StrategyStats)
         {
            Console.WriteLine("WinCount: "+fs.WinCount);
            Console.WriteLine("Alignment: "+fs.Alignment);
            Console.WriteLine("------------------");
         }

         // Save stats
         bool didHitMaxWins = 
            cur.OverallData.WinCount > _maxWins;
         bool didHitMaxFitness = 
            cur.Fitness > _maxFitness;
         _maxWins = Math.Max(_maxWins, cur.OverallData.WinCount);
         _maxFitness = Math.Max(_maxFitness, cur.Fitness);

         // Log the individuals
         _individualLog.LogIndividual(cur);
         if (didHitMaxWins)
            _championLog.LogIndividual(cur);
         if (didHitMaxFitness)
            _fittestLog.LogIndividual(cur);
      }
      
      public void Run()
      {
         _individualsEvaluated = 0;
         _maxWins = 0;
         _maxFitness = Int32.MinValue;
         _runningWorkers = new Queue<int>();
         _idleWorkers = new Queue<int>();
         
         string boxesDirectory = "boxes/";
         string inboxTemplate = boxesDirectory
            + "deck-{0,4:D4}-inbox.tml";
         string outboxTemplate = boxesDirectory
            + "deck-{0,4:D4}-outbox.tml";

         // Let the workers know we are here.
         string activeDirectory = "active/";
         string activeWorkerTemplate = activeDirectory
            + "worker-{0,4:D4}.txt";
         string activeSearchPath = activeDirectory
            + "search.txt";
         using (FileStream ow = File.Open(activeSearchPath,
                  FileMode.Create, FileAccess.Write, FileShare.None))
         {
            WriteText(ow, "MAP Elites");
            WriteText(ow, _configFilename);
            ow.Close();
         }

         Console.WriteLine("Begin search...");
         while (_individualsEvaluated < _params.NumToEvaluate)
         {
            // Look for new workers.
            string[] hailingFiles = Directory.GetFiles(activeDirectory);
            foreach (string activeFile in hailingFiles)
            {
               string prefix = activeDirectory + "worker-";
               if (activeFile.StartsWith(prefix))
               {
                  string suffix = ".txt";
                  int start = prefix.Length;
                  int end = activeFile.Length - suffix.Length;
                  string label = activeFile.Substring(start, end-start);
                  int workerId = Int32.Parse(label);
                  _idleWorkers.Enqueue(workerId);
                  _individualStable.Add(workerId, null);
                  File.Delete(activeFile);
                  Console.WriteLine("Found worker " + workerId);
               }
            }
            
            // Dispatch jobs to the available workers.
            while (_idleWorkers.Count > 0)
            {
               if (_individualsDispatched >= _params.InitialPopulation &&
                   _individualsEvaluated == 0)
               {
                  break;
               }

               int workerId = _idleWorkers.Dequeue();
               _runningWorkers.Enqueue(workerId);
               Console.WriteLine("Starting worker: "+workerId);
               
               Individual choiceIndividual =
                  _individualsDispatched < _params.InitialPopulation ? 
                     Individual.GenerateRandomIndividual(_cardSet) :
                     _featureMap.GetRandomElite().Mutate();

               string inboxPath = string.Format(inboxTemplate, workerId);
               SendWork(inboxPath, choiceIndividual);
               _individualStable[workerId] = choiceIndividual;
               _individualsDispatched++;
            }

            // Look for individuals that are done.
            int numActiveWorkers = _runningWorkers.Count;
            for (int i=0; i<numActiveWorkers; i++)
            {
               int workerId = _runningWorkers.Dequeue();
               string inboxPath = string.Format(inboxTemplate, workerId);
               string outboxPath = string.Format(outboxTemplate, workerId);

               // Test if this worker is done.
               if (File.Exists(outboxPath) && !File.Exists(inboxPath))
               {
                  // Wait for the file to finish being written.
                  Console.WriteLine("Worker done: " + workerId);

                  ReceiveResults(outboxPath, _individualStable[workerId]);
                  _featureMap.Add(_individualStable[workerId]);
                  _idleWorkers.Enqueue(workerId);
                  _individualsEvaluated++;
                  _map_log.UpdateLog();
               }
               else
               {
                  _runningWorkers.Enqueue(workerId);
               }
            }

            Thread.Sleep(1000);
         }
      
         // Let the workers know that we are done.
         File.Delete(activeSearchPath);
      }
   }
}
