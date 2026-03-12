using System;
using System.Collections.Generic;
using System.IO;
using Terraria;

namespace Terraria.PacketAnalysis
{
    // Interface for dependency tracking
    public class DependencyGraph
    {
        public Dictionary<string, object> Dependencies { get; } = new Dictionary<string, object>();
        public List<string> ReadOrder { get; } = new List<string>();

        public void Register(string name, object value)
        {
            Dependencies[name] = value;
            ReadOrder.Add(name);
        }

        public T Get<T>(string name)
        {
            if (Dependencies.TryGetValue(name, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default;
        }
    }

    public class PacketContext
    {
        public int WhoAmI { get; set; }
        public BinaryReader Reader { get; set; }
        public int MessageId { get; set; }
    }

    public class SideEffect
    {
        public Action Execute { get; set; }
        public string Description { get; set; }
    }

    public class ProcessingResult<TData>
    {
        public TData Data { get; set; }
        public List<SideEffect> SideEffects { get; } = new List<SideEffect>();
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
    }

    public interface IPacketProcessor
    {
        void Process(PacketContext context);
    }

    public abstract class BasePacketProcessor<TDependency, TRawData, TProcessedData> : IPacketProcessor
        where TDependency : class
        where TRawData : class
        where TProcessedData : class
    {
        // Metric collection
        public Dictionary<string, object> Metrics { get; } = new Dictionary<string, object>();

        public void Process(PacketContext context)
        {
            var startTime = DateTime.Now;

            // Stage 1: Dependency Reading
            var dependency = Stage1_ReadDependencies(context);
            Metrics["Stage1_Time"] = (DateTime.Now - startTime).TotalMilliseconds;
            
            // Stage 2: Ordinary Flow Reading
            var rawData = Stage2_ReadOrdinaryFlow(context, dependency);
            Metrics["Stage2_Time"] = (DateTime.Now - startTime).TotalMilliseconds; // Cumulative

            // Stage 3: Logical Processing
            var result = Stage3_ProcessLogic(rawData, dependency, context);
            Metrics["Stage3_Time"] = (DateTime.Now - startTime).TotalMilliseconds; // Cumulative

            // Stage 4: Side Effects
            Stage4_ExecuteSideEffects(result, context);
            Metrics["Total_Time"] = (DateTime.Now - startTime).TotalMilliseconds;
            
            // Generate Report (Optional)
            GenerateReport(context, dependency, rawData, result);
        }

        protected abstract TDependency Stage1_ReadDependencies(PacketContext context);
        protected abstract TRawData Stage2_ReadOrdinaryFlow(PacketContext context, TDependency dependency);
        protected abstract ProcessingResult<TProcessedData> Stage3_ProcessLogic(TRawData rawData, TDependency dependency, PacketContext context);
        protected abstract void Stage4_ExecuteSideEffects(ProcessingResult<TProcessedData> result, PacketContext context);
        
        protected virtual void GenerateReport(PacketContext context, TDependency dependency, TRawData rawData, ProcessingResult<TProcessedData> result)
        {
            // Simple console log for now
            // Console.WriteLine($"Packet {context.MessageId} processed in {Metrics["Total_Time"]}ms");
        }
    }
}
