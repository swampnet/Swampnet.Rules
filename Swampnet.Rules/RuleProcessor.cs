﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Swampnet.Rules
{
    public class RuleProcessor<T>
    {
		public class RuleProcessorResult
		{
			private static long _id = 0;

			internal RuleProcessorResult(Rule rule, bool result)
			{
				Timestamp = DateTime.UtcNow;
				Rule = rule;
				Result = result;
				Id = ++_id;
			}

			internal Rule Rule { get; private set; }
			internal long Id { get; set; }
			public DateTime Timestamp { get; private set; }
			public bool Result { get; private set; }

			public override string ToString()
			{
				return $"[Id] [{Timestamp.ToLongTimeString()}] {Result}";
			}
		}


		private readonly Func<ActionDefinition, Action<T, Rule, ActionDefinition>> _resolver;
		private readonly Evaluator<T> _evaluator;
		private readonly List<RuleProcessorResult> _results = new List<RuleProcessorResult>();


		// Action<T, Rule<T>	
		// resolver:
		//	Func - Takes an ActionDefinition and returns an Action<T, Rule<T>>
		public RuleProcessor(
			Func<ActionDefinition, Action<T, Rule, ActionDefinition>> actionResolver,
			Evaluator<T> evaluator)
		{
			_resolver = actionResolver;
			_evaluator = evaluator;
		}


		public void Run(T context, Rule rule)
		{
			// Evaluate expression
			var result = _evaluator.Evaluate(context, rule.Expression);

			// Save result 
			SaveResult(result, rule);

			// Get consectutive hits
			var history = GetHistory(rule);

			var consecutiveHits = history
				.OrderByDescending(h => h.Timestamp).ThenByDescending(h => h.Id)
				.TakeWhile(h => h.Result == result)
				.OrderBy(h => h.Timestamp).ThenBy(h => h.Id);

			// Process any true/false actions
			ProcessActions(context, rule, result ? rule.TrueActions : rule.FalseActions, consecutiveHits);
		}


		public IEnumerable<RuleProcessorResult> GetHistory(Rule rule)
		{
			return _results
				.Where(r => r.Rule == rule)
				.OrderBy(r => r.Timestamp)
				.ThenBy(r => r.Id);
		}


		private void SaveResult(bool result, Rule rule)
		{
			_results.Add(new RuleProcessorResult(rule, result));

			var expiredRuleResults = GetHistory(rule)
				.OrderByDescending(r => r.Timestamp)        // newest first
				.Skip(rule.MaxHistoryRequired);             // Skip over required

			// ...remove the rest
			foreach (var expired in expiredRuleResults)
			{
				_results.Remove(expired);
			}
		}

		private void ProcessActions(T context, Rule rule, IEnumerable<ActionDefinition> actionDefinitions, IEnumerable<RuleProcessorResult> consecutiveHits)
		{
			if (actionDefinitions != null)
			{
				foreach (var definition in actionDefinitions)
				{
					if(consecutiveHits.Count() >= definition.CosecutiveHits)
					{
						Trace.WriteLine($"'{definition.Name}' fired (consecutive hits: {consecutiveHits.Count()} / {definition.CosecutiveHits})");
						try
						{
							_resolver
								.Invoke(definition)                     // Get Action<T, Rule<T>, ActionDefinition>
								.Invoke(context, rule, definition);     // And execute it
						}
						catch (Exception ex)
						{
							Trace.TraceError($"{definition.Name} threw error: " + ex.Message);
						}
					}
				}
			}
		}
	}
}
