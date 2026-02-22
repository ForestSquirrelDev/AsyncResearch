namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public class WaitAsyncExample
    {
        public static async Task RunWaitAsyncExample()
        {
            var heavyTask = DoHeavyWorkAsync();

            try
            {
                await heavyTask.WaitAsync(TimeSpan.FromMilliseconds(500));
                Console.WriteLine("Работа завершена вовремя");
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Упс, таймаут ожидания! Уходим...");
            }
        }

        private static async Task DoHeavyWorkAsync()
        {
            await Task.Delay(1000);
            throw new InvalidOperationException("OH MY GOD!");
        }
    }
}