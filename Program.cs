using AsyncResearch.Chapter_5_Instruments.Source;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        { 
            await TaskDelayCancellationTokenExample.RunTaskDelayCancellationTokenExample();
        }
    }
}