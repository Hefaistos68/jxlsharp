# Project JXL#

The goal of this project is to make a pure C#/Dotnet version of the libjxl project in the parent folder.

We can use the already existing csharp version of the brotli library. It is a big project and we should take our time to implement it correctly. We should aim for a 1-to-1 implementation of the C++ code, but in C#. If there are any obvious improvements we can make in C#, we should consider them, but we should not stray too far from the original implementation.

The project will be split into several smaller projects, each corresponding to a module in the C++ project.

We need to fully pass all tests in the c++ version before we can even consider this project done. 

Any part that is not dotnet ecosystem related can be skipped (like debian) 

Make a full plan for implementation, with 90% code coverage on all C# sources.