using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// No components: the page is three.js and plain HTML; it calls Viewer's [JSInvokable] methods once the runtime starts.
await WebAssemblyHostBuilder.CreateDefault(args).Build().RunAsync();
