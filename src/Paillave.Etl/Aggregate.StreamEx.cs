using Paillave.Etl.Core;
using Paillave.Etl.StreamNodes;
using Paillave.Etl.Core.Streams;
using Paillave.Etl.Core.TraceContents;
using Paillave.Etl.ValuesProviders;
using Paillave.Etl.Reactive.Core;
using Paillave.Etl.Reactive.Operators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using SystemIO = System.IO;

namespace Paillave.Etl
{
    public static partial class AggregateEx
    {
        /// <summary>
        /// Aggregate every element of a stream into a list of aggregations computed for each group by the key
        /// </summary>
        /// <param name="stream">Input stream</param>
        /// <param name="name">Name of the operation</param>
        /// <param name="emptyAggregation">Initial value of the aggregation per key</param>
        /// <param name="getKey">Method to get the key of an element of the stream</param>
        /// <param name="aggregate">Aggregator that will receive the current value of the aggregation for the key value of the current element and that must return the new aggregation value</param>
        /// <typeparam name="TIn">Main stream type</typeparam>
        /// <typeparam name="TAggr">Aggregation type</typeparam>
        /// <typeparam name="TKey">Key type</typeparam>
        /// <returns>Output type</returns>
        /// <example>
        /// This example computes the average of every element grouped by their modulo 3.
        /// <code>
        /// public class ComputeAverageJob : IStreamProcessDefinition&lt;object&gt;
        /// {
        ///     public string Name => "import file";
        ///     public void DefineProcess(IStream&lt;object&gt; rootStream)
        ///     {
        ///         rootStream
        ///             .CrossApplyEnumerable("create some values", (input) => Enumerable.Range(0, 10))
        ///             .Aggregate("aggregate values for average computation",
        ///                 i => new { sum = 0, nb = 0 },
        ///                 i => i % 3,
        ///                 (previousAggr, value) => new { sum = previousAggr.sum + value, nb = previousAggr.nb + 1 })
        ///             .Select("compute average", i => new { i.Key, Avg = i.Aggregation.sum / i.Aggregation.nb });
        ///     }
        /// }
        /// </code>
        /// </example>
        public static IStream<AggregationResult<TIn, TKey, TAggr>> Aggregate<TIn, TAggr, TKey>(this IStream<TIn> stream, string name, Func<TIn, TAggr> emptyAggregation, Func<TIn, TKey> getKey, Func<TAggr, TIn, TAggr> aggregate)
        {
            return new AggregateStreamNode<TIn, TAggr, TKey>(name, new AggregateArgs<TIn, TAggr, TKey>
            {
                InputStream = stream,
                Aggregate = aggregate,
                GetKey = getKey,
                CreateEmptyAggregation = emptyAggregation,
            }).Output;
        }
        /// <summary>
        /// Aggregate every element of a sorted stream into a list of aggregations computed for each group by the sorting key
        /// </summary>
        /// <param name="stream">Sorted input stream</param>
        /// <param name="name">Name of the operation</param>
        /// <param name="emptyAggregation">Initial value of the aggregation per key</param>
        /// <param name="aggregate">Aggregator that will receive the current value of the aggregation for the key value of the current element and that must return the new aggregation value</param>
        /// <typeparam name="TIn">Main stream type</typeparam>
        /// <typeparam name="TAggr">Aggregation type</typeparam>
        /// <typeparam name="TKey">Key type</typeparam>
        /// <returns>Output type</returns>
        public static ISortedStream<AggregationResult<TIn, TKey, TAggr>, TKey> Aggregate<TIn, TAggr, TKey>(this ISortedStream<TIn, TKey> stream, string name, Func<TIn, TAggr> emptyAggregation, Func<TAggr, TIn, TAggr> aggregate)
        {
            return new AggregateSortedStreamNode<TIn, TAggr, TKey>(name, new AggregateSortedArgs<TIn, TAggr, TKey>
            {
                InputStream = stream,
                Aggregate = aggregate,
                CreateEmptyAggregation = emptyAggregation
            }).Output;
        }
    }
}