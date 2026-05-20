import React from 'react';

const App: React.FC = () => {
    return (
        <div className="flex flex-col items-center justify-center h-screen bg-black text-white font-sans">
            <h1 className="text-4xl font-bold mb-4">StreamMesh P2P</h1>
            <p className="text-lg text-gray-400">Web Dashboard & Player</p>
            <div className="mt-8 p-6 bg-zinc-900 rounded-2xl border border-white/10 shadow-md">
                <p className="text-sm text-zinc-500">
                    Python backend is running. This web interface will connect to the P2P network.
                </p>
            </div>
        </div>
    );
};

export default App;
