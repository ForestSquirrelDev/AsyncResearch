namespace AsyncResearch.AsyncExperiments.Chapter_1_HeartOfTheStateMachine.Source
{
    public static class ContinuationExample
    {
        public static async Task Test()
        {
            Console.WriteLine("Start");
            await DoWorkAsync();
            Console.WriteLine("End");
        }
        
        public static async Task DoWorkAsync()
        {
            var localVariable = 42;
            await Task.Delay(10000);
            Console.WriteLine("Resumed with " + localVariable);
        }
    }
}