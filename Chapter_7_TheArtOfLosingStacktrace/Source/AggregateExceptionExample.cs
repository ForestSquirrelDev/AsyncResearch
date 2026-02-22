namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class AggregateExceptionExample
    {
        public static void AggregateExceptionTest()
        {
            try
            {
                Layer0().Wait();
            }
            catch (AggregateException ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static async Task Layer0()
        {
            await Task.Delay(100);
            await Layer1();
        }

        private static async Task Layer1()
        {
            await Task.Delay(100);
            await Layer2();
        }
        
        private static async Task Layer2()
        {
            await Task.Delay(100);
            await Layer3();
        }

        private static async Task Layer3()
        {
            await Task.Delay(100);
            await Layer4();
        }
        
        private static async Task Layer4()
        {
            await Task.Delay(100);
            throw new Exception("HORY SHIET!");
        }
    }
}