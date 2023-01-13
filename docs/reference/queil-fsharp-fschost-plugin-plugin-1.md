## Plugin<'a> Type

Namespace: [Queil.FSharp.FscHost](http://localhost:8089/fsc-host/reference/queil-fsharp-fschost)

Assembly: Queil.FSharp.FscHost.dll

Parent Module: [Plugin](http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin)

Base Type: <code>obj</code>



### Constructors

Constructor | Description | Source
:--- | :--- | :---:
[<code><span>Plugin<span><span>(<span>state</span>)</span></span></span></code>](#%60%60.ctor%60%60) | Parameters<br /><br />**state**: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-pluginoptions">PluginOptions</a></code><br /><br />Returns: <code><span><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-plugin-1">Plugin</a>&lt;'a&gt;</span></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L31-31)


### Instance members

Instance member | Description | Source
:--- | :--- | :---:
[<code><span>binding&#32;<span>name</span></span></code>](#Binding) | Defines the name of a binding to extract. Default: plugin<br /><br />Parameters<br /><br />**name**: <code>string</code><br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-commonbuilder">CommonBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L72-72)
[<code><span>body&#32;<span>script</span></span></code>](#Body) | Defines the body of a script to compile<br /><br />Parameters<br /><br />**script**: <code>string</code><br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-bodybuilder">BodyBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L94-94)
[<code><span>cache&#32;<span>useCache</span></span></code>](#Cache) | Controls script caching behaviour. Default: caching is off<br /><br />Parameters<br /><br />**useCache**: <code>bool</code><br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-commonbuilder">CommonBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L54-54)
[<code><span>cache_dir&#32;<span>cacheDir</span></span></code>](#CacheDir) | Overrides the default cache dir path. It is only relevant if cache is enabled. Default: .fsc-host/cache<br /><br />Parameters<br /><br />**cacheDir**: <code>string</code><br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-commonbuilder">CommonBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L60-60)
[<code><span>compiler&#32;<span>configure</span></span></code>](#Compiler) | Enables customization of a subset of compiler options<br /><br />Parameters<br /><br />**configure**: <code><span><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-compileroptions">CompilerOptions</a>&#32;->&#32;<a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-compileroptions">CompilerOptions</a></span></code><br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-commonbuilder">CommonBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L77-77)
[<code><span>dir&#32;<span>dir</span></span></code>](#Dir) | The directory plugin gets loaded from. Default: plugins/default<br /><br />Parameters<br /><br />**dir**: <code>string</code><br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-filebuilder">FileBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L84-84)
[<code><span>file&#32;<span>file</span></span></code>](#File) | Sets plugin script file name. Default: plugin.fsx<br /><br />Parameters<br /><br />**file**: <code>string</code><br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-filebuilder">FileBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L89-89)
[<code><span>load&#32;<span></span></span></code>](#Load) | Loads a plugin with default configuration. <br /> It expects ./plugins/default/plugin.fsx with 'let plugin = ... ' binding matching the<br /> specified plugin type.<br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-filebuilder">FileBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L101-101)
[<code><span>log&#32;<span>logFun</span></span></code>](#Log) | Enables a custom logging function<br /><br />Parameters<br /><br />**logFun**: <code><span>string&#32;->&#32;unit</span></code><br /><br />Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-commonbuilder">CommonBuilder</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L66-66)
[<code><span>this.Run</span></code>](#Run) | Parameters<br /><br />**state**: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-commonbuilder">CommonBuilder</a></code><br /><br />Returns: <code><span><a href="https://fsharp.github.io/fsharp-core-docs/reference/fsharp-control-fsharpasync-1">Async</a>&lt;'a&gt;</span></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L35-35)
[<code><span>this.State</span></code>](#State) | Returns: <code><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-pluginoptions">PluginOptions</a></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L33-33)
[<code><span>this.Yield</span></code>](#Yield) | Parameters<br /><br />**arg0**: <code>'b</code><br /><br />Returns: <code><span><a href="http://localhost:8089/fsc-host/reference/queil-fsharp-fschost-plugin-plugin-1">Plugin</a>&lt;'a&gt;</span></code><br /> | [![Link to source code](http://localhost:8089/fsc-host/content/img/github.png)](https://github.com/queil/fsc-host/tree/main/src/Queil.FSharp.FscHost/Plugin.fs#L34-34)



