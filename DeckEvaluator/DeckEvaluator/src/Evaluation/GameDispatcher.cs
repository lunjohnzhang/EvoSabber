using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SabberStoneCore.Enums;
using SabberStoneCore.Model;

using DeckEvaluator.Evaluation;

namespace DeckEvaluator.Evaluation
{
   class GameDispatcher
   {
      private int _numGames;
      private int _numActive;
      private CardClass _opponentClass;
      private List<Card> _opponentDeck;
      private CardClass _playerClass;
		private List<Card> _playerDeck;

      // Total stats for all the games played.
		private readonly object _statsLock = new object();
      private int _winCount;
      private Dictionary<string,int> _usageCount;
      private int _totalHealthDifference;
      private int _totalDamage;
      private int _totalTurns;
      private int _totalCardsDrawn;
      private int _totalManaSpent;
      private int _totalStrategyAlignment;

		public GameDispatcher(int numGames, CardClass playerClass,
                            List<Card> playerDeck, 
                            CardClass opponentClass,
                            List<Card> opponentDeck)
		{
         // Save the configuration information.
         _numGames = numGames;
         _playerClass = playerClass;
         _playerDeck = playerDeck;
         _opponentClass = opponentClass;
         _opponentDeck = opponentDeck;
         _numActive = numGames;
      
         // Setup the statistics keeping.
         _winCount = 0;
         _usageCount = new Dictionary<string,int>();
         _totalDamage = 0;
         _totalHealthDifference = 0;
         _totalTurns = 0;
         _totalCardsDrawn = 0;
         _totalManaSpent = 0;
         _totalStrategyAlignment = 0;
         foreach (Card curCard in playerDeck)
         {
				if (!_usageCount.ContainsKey(curCard.Name))
				{
					_usageCount.Add(curCard.Name, 0);
				}
         }
      }

      private void runGame(int gameId, GameEvaluator ev)
      {
         Console.WriteLine("Starting game: "+gameId);

         // Run a game
         GameEvaluator.GameResult result = ev.PlayGame();
         
         // Record stats
         lock (_statsLock)
         {
            if (result._didWin)
            {
               _winCount++;
            }
            
	         foreach (string cardName in result._cardUsage.Keys)
            {
               if (_usageCount.ContainsKey(cardName))
               {
                  _usageCount[cardName] += result._cardUsage[cardName];
               }
            }
  
            _totalHealthDifference += result._healthDifference;
            _totalDamage += result._damageDone;
            _totalTurns += result._numTurns;
            _totalCardsDrawn += result._cardsDrawn;
            _totalManaSpent += result._manaSpent;
            _totalStrategyAlignment += result._strategyAlignment;
            _numActive--;
         }

         Console.WriteLine("Finished game: "+gameId);
      }

      private void queueGame(int gameId)
      {
         var playerDeck = new List<Card>(_playerDeck);
         var opponentDeck = new List<Card>(_opponentDeck);

      	var ev = new GameEvaluator(_playerClass, playerDeck, 
               _opponentClass, opponentDeck);
         runGame(gameId, ev);
      }

      private void WriteText(Stream fs, string s)
      {
         s += "\n";
         byte[] info = new UTF8Encoding(true).GetBytes(s);
         fs.Write(info, 0, info.Length);
      }

      public void Run(string resultsFilename)
      {
			// Queue up the games
         _numActive = _numGames;
         Parallel.For(0, _numGames, i => {queueGame(i);});
         //for (int i=0; i<_numGames; i++)
         //   queueGame(i);

         // Output the results to the output file.
			using (FileStream ow = File.Open(resultsFilename, 
                   FileMode.Create, FileAccess.Write, FileShare.None))
         {
            List<string> outputDeck =
               _playerDeck.ConvertAll<string>(a => a.Name);
            List<int> usageCounts =
               outputDeck.ConvertAll<int>(a => _usageCount[a]);
            WriteText(ow, string.Join("*", outputDeck));
            WriteText(ow, string.Join("*", usageCounts));
            WriteText(ow, _winCount.ToString());
            WriteText(ow, _totalHealthDifference.ToString());
            WriteText(ow, _totalDamage.ToString());
            WriteText(ow, _totalTurns.ToString());
            WriteText(ow, _totalCardsDrawn.ToString());
            WriteText(ow, _totalManaSpent.ToString());
            WriteText(ow, _totalStrategyAlignment.ToString());
            ow.Close();
         }
      }
   }
}
