using System.Diagnostics;

namespace AsyncResearch.AsyncExperiments.Chapter_5_Instruments.Source
{
    public static class TaskCompletionSourceExample
    {
        public static async Task Test()
        {
            var tcs = new TaskCompletionSource();
            var task = DoWorkAsync(tcs);
            tcs.SetResult();
            await task;
        }

        private static async Task DoWorkAsync(TaskCompletionSource tcs)
        {
            Console.WriteLine("DoWorkAsync: before await");
            await tcs.Task;
            Console.WriteLine($"DoWorkAsync: after await, stack trace {new StackTrace()}");
        }
    }
}