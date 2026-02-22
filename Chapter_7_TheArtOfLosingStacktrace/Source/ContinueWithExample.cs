namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class ContinueWithExample
    {
        public static async Task RunContinueWithExample()
        {
            var task = Task.Run(() => throw new Exception("Die!"))
                .ContinueWith(t => Console.WriteLine("I'm running anyway!"));
            await task;
        }
        
        public static async Task RunManualExceptionHandlingExample()
        {
            var task = Task.Run(() => throw new Exception("Die!"))
                .ContinueWith(t => Console.WriteLine($"Oh no, exception occured! {t.Exception}"));
            await task;
        }
        
        public static async Task RanToCompletionExample()
        {
            var task = Task.Run(() => throw new Exception("Die!"))
                .ContinueWith(t => Console.WriteLine($"I'm running anyway!"), TaskContinuationOptions.OnlyOnRanToCompletion);
            await task;
        }
    }
}