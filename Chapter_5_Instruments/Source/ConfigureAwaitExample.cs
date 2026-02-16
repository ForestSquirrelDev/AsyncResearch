namespace AsyncResearch.AsyncExperiments.Chapter_5_Instruments.Source
{
    public static class ConfigureAwaitExample
    {
        public static async Task Test()
        {
            Console.WriteLine("Test: Start");
            try
            {
                await DoWorkAsync().ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            Console.WriteLine("Test: End");
        }
        
        private static async Task DoWorkAsync()
        {
            var localVariable = 42;
            await Task.Delay(1000);
            Console.WriteLine("Resumed with " + localVariable);
            throw new Exception("DoWorkAsync: HORY SHIET!");
        }
    }
}