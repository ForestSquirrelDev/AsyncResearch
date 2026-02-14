namespace AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class TaskWhenAnyExample
    {
        public static async Task WhenAnyExceptionsExample()
        {
            var t1 = Exception1();
            var t2 = Exception2();
            var t3 = Exception3();
            
            var any = Task.WhenAny(t1, t2, t3);
            try
            {
                await any;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static async Task Exception1()
        {
            throw new Exception("Exception1");
        }

        private static async Task Exception2()
        {
            throw new Exception("Exception2");
        }

        private static async Task Exception3()
        {
            throw new Exception("Exception3");
        }
    }
}