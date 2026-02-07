namespace AsyncResearch.AsyncExperiments.Chapter_X_Additionals
{
    public static class ForceYieldingInfiniteLoopExample
    {
        public static async Task Test()
        {
            var task = Task.CompletedTask;
            var awaiter = task.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            while (!awaiter.GetAwaiter().IsCompleted)
            {
                Console.WriteLine("Oh no! Infinite loop!");
                await Task.Delay(100);
            }
        }
    }
}