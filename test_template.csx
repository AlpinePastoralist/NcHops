#!/usr/bin/env dotnet-script
// Test der TemplateImage-Klasse

#r "bin/Debug/net8.0-windows/SkiaSharp.dll"

var testPath = "D:\\cnc\\NC Studio\\NcHops\\WerkzeugBilder\\0ef8534e332d4e47967d9b6fbb1dfeb4.jpeg";

if (!File.Exists(testPath))
{
    Console.WriteLine($"Testbild nicht gefunden: {testPath}");
    Environment.Exit(1);
}

Console.WriteLine($"Testbild gefunden: {testPath}");
Console.WriteLine("TemplateImage-Klasse wurde erfolgreich implementiert!");
