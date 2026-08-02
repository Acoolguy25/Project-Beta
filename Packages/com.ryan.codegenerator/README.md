# Run the following to create the project and add the package

```
dotnet new classlib -n CodeGen -f netstandard2.0
cd CodeGen
dotnet add package Microsoft.CodeAnalysis.CSharp --version 4.8.0
dotnet add package Microsoft.CodeAnalysis.Analyzers --version 3.3.4
```
