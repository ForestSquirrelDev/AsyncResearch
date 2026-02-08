using AsyncResearch.AsyncExperiments.Chapter_5_Additionals;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await TaskCompletionSourceExample.Test();
        }
    }
}