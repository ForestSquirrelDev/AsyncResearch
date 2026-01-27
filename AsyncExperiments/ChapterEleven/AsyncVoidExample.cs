namespace AsyncResearch.AsyncExperiments.ChapterEleven
{
    public static class AsyncVoidExample
    {
        // Chapter 11: Where (in what context) does the state machine for an async void task get created, and why is it the "Hand grenade" of C#?
        public static void Test()
        {
            Console.WriteLine("Start");
            DoWorkAsync();
            Console.WriteLine("End");
        }
        
        public static async void DoWorkAsync()
        {
            var localVariable = 42;
            await Task.Delay(10000);
            Console.WriteLine("Resumed with " + localVariable);
        }
    }
}