import React from 'react';

const logo = new URL('./logos/StreamMesh_logo.png', import.meta.url).href;

const App: React.FC = () => {
    return (
        <div className="flex flex-col items-center justify-center h-screen bg-black text-white font-sans p-4">
            <img src={logo} alt="StreamMesh Logo" className="w-24 h-24 mb-6 object-contain rounded-2xl shadow-lg border border-white/10" referrerPolicy="no-referrer" />
            <h1 className="text-4xl font-bold mb-2">StreamMesh</h1>
            <p className="text-lg text-gray-400 mb-6">Web Kontrol Paneli &amp; Medya Merkezi</p>
            <div className="max-w-md p-6 bg-zinc-900 rounded-2xl border border-white/10 shadow-md text-center">
                <p className="text-sm text-zinc-400">
                    WPF uygulaması için uzaktan kontrol, oynatıcı ve kanal yönetim servisleri aktiftir.
                </p>
            </div>
        </div>
    );
};

export default App;
