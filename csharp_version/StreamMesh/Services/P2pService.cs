using System;
using System.Threading.Tasks;

namespace StreamMesh.Services
{
    public class P2pService
    {
        public bool IsActive { get; private set; }

        public async Task StartServiceAsync()
        {
            // Placeholder: Initialize local P2P node
            IsActive = true;
            Console.WriteLine("P2P Node started.");
            await Task.Delay(100);
        }

        public void StopService()
        {
            IsActive = false;
            Console.WriteLine("P2P Node stopped.");
        }
    }
}
