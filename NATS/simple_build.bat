REM A very simple build batch file.

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

%CSC% /out:bin\Release\NATS.Client.DLL /target:library /doc:NATS.XML /optimize+ *.cs Properties\*.cs
%CSC% /out:bin\Debug\NATS.Client.DLL /target:library /debug+ *.cs Properties\*.cs

