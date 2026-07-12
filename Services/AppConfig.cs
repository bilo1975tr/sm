namespace StreamMesh.Services
{
    public static class AppConfig
    {
        public const string GitHubRepoUrl = "https://raw.githubusercontent.com/bilo1975tr/sm/main";
        public const string FirebaseDatabaseUrl = "https://streammesh-p2p-default-rtdb.europe-west1.firebasedatabase.app";
        
        public static int FirebaseBatchSize => 10;

        public static string GetGitHubLanguageUrl(string safeLang)
        {
            return $"{GitHubRepoUrl}/channels_{safeLang}.json";
        }

        public static string GetFirebasePoolUrl()
        {
            return $"{FirebaseDatabaseUrl}/new_channels.json";
        }
    }
}
