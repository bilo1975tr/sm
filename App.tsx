import React from 'react';

const App: React.FC = () => {
    return (
        <div className="flex flex-col items-center justify-center h-screen bg-black text-white font-sans">
            <h1 className="text-4xl font-bold mb-4">StreamMesh</h1>
            <p className="text-lg text-gray-400">Web Kontrol Paneli &amp; Medya Merkezi</p>
            <div className="mt-8 p-6 bg-zinc-900 rounded-2xl border border-white/10 shadow-md">
                <p className="text-sm text-zinc-500">
                    WPF uygulaması için uzaktan kontrol, oynatıcı ve kanal yönetim servisleri aktiftir.
                </p>
            </div>
        </div>
    );
};

export default App;
