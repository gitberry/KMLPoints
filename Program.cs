using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace KMLExtractPoints
{
    /// <summary>
    /// 
    /// KMLExtractPoints: a simple windows .net app to extract points from a KML file.  
    /// 
    /// </summary>
    //todo: make this more automatable - perhaps "pipe"able - this particular version outputs files that need further processing 
    //      that was handled in a spreadsheet and manual processing 
    //todo: JSON 
    //todo: feed into a specified API (securely of course)
 
    class Program
    {
        static string ParamInput = "/KML:";
        static string ParamOutput = "/OUT:";
        static string ParamJSON = "/JSON:";  // future stuff - needs testing..
        static string emptyString = "";
        static bool consoleInOut = false; // for future mods to allow pipe and redirection friendly calling of this app for best automation options...

        static void Main(string[] args)
        {
            string pInputFile = getParam(args, ParamInput, emptyString);
            string pOutputFile = getParam(args, ParamOutput, emptyString);
            string JSONOutput = getParam(args, ParamJSON, "N");
            bool ExportJSON = (JSONOutput.ToUpper() == "Y");

            //todo: make this pipe friendly ie: type myfile.kml > KMLPointsExtract /JSON:Y | KMLPointsGetW3W  > Final.JSON
            if (pInputFile == emptyString || pOutputFile == emptyString) { HelpUsage("Requires Parameters"); }

            Console.Write($"\nKML Input: {pInputFile}"); if (!File.Exists(pInputFile)) { HelpUsage($"The Input file [{pInputFile}] does not exist.\n"); }
            Console.Write($"\nKML Output: {pOutputFile}");
            if (File.Exists(pOutputFile))
            {
                Console.WriteLine($"The Output file [{pOutputFile}] already exists - overwrite? Y/N");
                ConsoleKeyInfo UserResponse = Console.ReadKey();
                if (UserResponse.Key.ToString().ToUpper() == "Y")
                {
                    File.Delete(pOutputFile);
                }
                else
                {
                    HelpUsage($"The Output file [{pOutputFile}] exists and you have chosen to not overwrite it.\n");
                }
            }

            // set up xml to read KML (learned from here: https://stackoverflow.com/questions/8822402/read-a-kml-file-on-c-sharp-project-to-give-back-the-information)
            var xDoc = XDocument.Load(pInputFile);
            string xNs = "{" + xDoc.Root.Name.Namespace.ToString() + "}"; // don't know what the namespace is - don't care - just need to use it to get our data...

            //array place to store info
            List<ExtractPlacemark> MyExtracts = new List<ExtractPlacemark>();

            foreach (XElement e1 in (from f in xDoc.Descendants(xNs + "Placemark") select f))
            {
                ExtractPlacemark thisPlaceMark = new ExtractPlacemark();

                // get placemarks
                foreach (XElement e2 in (from f in e1.Elements() where f.Name == xNs + "name" select f))
                {
                    thisPlaceMark.Name = $"{e2.Value}";
                    break; // assume 1st is the one and only name element
                }

                // extract point (inside another element etc..)
                foreach (XElement e2 in (from f in e1.Elements() where f.Name == xNs + "Point" select f))
                {
                    foreach (XElement e3 in (from f in e2.Elements() where f.Name == xNs + "coordinates" select f))
                    {
                        string[] coordArray = $"{e3.Value}".Split(",");
                        //note: KML from google puts the LON first then the LAT - which is inverse of general convention... boot to the head
                        if (coordArray.Length > 0 && coordArray[0].IsNumeric()) { thisPlaceMark.Lon = coordArray[0].ToDouble(); }
                        if (coordArray.Length > 1 && coordArray[1].IsNumeric()) { thisPlaceMark.Lat = coordArray[1].ToDouble(); }
                        if (coordArray.Length > 2 && coordArray[2].IsNumeric()) { thisPlaceMark.Elev = coordArray[2].ToDouble(); }
                        break; // assume 1 point for a placemark...
                    }
                }
                // extract description 
                foreach (XElement e2 in (from f in e1.Elements() where f.Name == xNs + "description" select f))
                {
                    thisPlaceMark.Description = StripBeforeTwoBreaks(e2.Value);
                    break; // again assume only 1 description...
                }
                // Image URL's (like points = it is inside another element - AND has wonky formatting...)
                foreach (XElement e2 in (from f in e1.Elements() where f.Name == xNs + "ExtendedData" select f))
                {
                    foreach (XElement e3 in (from f in e2.Elements() where f.Name == xNs + "Data" select f))
                    {
                        foreach (XElement e4 in (from f in e3.Elements() where f.Name == xNs + "value" select f))
                        {
                            string[] RawData = $"{e4.Value}".Split(" ");
                            foreach (string thisUrl in RawData)
                            {
                                if (thisUrl != "") { thisPlaceMark.ImageURLsAdd(thisUrl); }
                            }
                            break;
                        }
                        break; //again - first only
                    }
                }
                MyExtracts.Add(thisPlaceMark);
            }

            if (ExportJSON)
            {
                // generate hash ID's - then only export unique list...
                List<string> ExtractHashes = new List<string>();
                foreach (ExtractPlacemark thisPlaceMark in MyExtracts)
                {
                    var thisHash = HashObj(thisPlaceMark);
                    if (!ExtractHashes.Exists(givenHash => (givenHash == thisHash)))
                    {
                        thisPlaceMark.ID = thisHash;
                        ExtractHashes.Add(thisHash);
                    }
                }
                string jsonExport = JsonSerializer.Serialize(MyExtracts.Where(placeMark => !string.IsNullOrEmpty(placeMark.ID)));
                if (consoleInOut)
                {
                    Console.Write(jsonExport);
                }
                else
                {
                    File.WriteAllText(pOutputFile, jsonExport);
                }
            }
            else
            {
                //header when doing text dump
                if (consoleInOut) { Console.WriteLine("\n" + ExtractPlacemark.ExportHeader()); }
                else { File.WriteAllText(pOutputFile, $"\n" + ExtractPlacemark.ExportHeader()); }

                //the data
                foreach (ExtractPlacemark thisPlace in MyExtracts)
                {
                    //todo: make ExportLine configurable with arguements
                    if (consoleInOut) { Console.WriteLine(thisPlace.ExportLine()); }
                    else { File.AppendAllText(pOutputFile, thisPlace.ExportLine()); }
                }
            }
        }

        static void HelpUsage(string givenVerbage = null)
        {
            if (!string.IsNullOrEmpty(givenVerbage)) { Console.WriteLine(givenVerbage); }
            Console.WriteLine("Usage: /KML:[file] /OUT:[file]");
            Console.ReadKey();
            Environment.Exit(-1);
        }


        static string getParam(string[] args, string givenPrefix, string defaultValue)
        {
            string result = defaultValue;
            for (int n = 0; n < args.Length; n++)
            {
                if (args[n].Substring(0, givenPrefix.Length) == givenPrefix)  // note: case sensitive
                {
                    result = args[n].Substring(givenPrefix.Length, args[n].Length - givenPrefix.Length);
                    break;
                }
            }
            return result;
        }

        public class ExtractPlacemark
        {
            public string ID { get; set; } // has to be set externally - sha256 hash of serialization of itself before ID is set.. (unless I think of a better way)
            public string Name { get; set; }
            public string Description { get; set; }
            public double Lat { get; set; }
            public double Lon { get; set; }
            public double Elev { get; set; }
            public List<string> ImageURLS { get; set; }

            // non serializable parts of the class:
            public void ImageURLsAdd(string givenImageURL)
            {
                if (this.ImageURLS == null) { this.ImageURLS = new List<string>(); }
                this.ImageURLS.Add(givenImageURL);
            }
            public string LatLon() { return $"Lat:{Lat},Lon:{Lon}"; }
            public string ExportLine(bool ShowHeader = false)
            {
                return $"{this.Name}{Delimiter}{this.Description}{Delimiter}{this.LatLon()}";
            }

            static public string ExportHeader()
            {
                return $"Name{Delimiter}Description{Delimiter}LatLon";
            }
            static public string Delimiter = "|";
        }

        private static string HashObj(object givenObject)
        {
            return HashString(JsonSerializer.Serialize(givenObject)); //question: did we just reinvent a wheel somehow?
        }
        //example from: https://www.c-sharpcorner.com/article/compute-sha256-hash-in-c-sharp/
        private static string HashString(string givenString)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(givenString));

                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static string StripBeforeTwoBreaks(string givenString)
        {
            string breaks = "<br><br>";
            string result = ""; // givenString;
            if (givenString.Contains(breaks))
            {
                result = givenString.Substring(givenString.IndexOf(breaks) + breaks.Length);
                // in case something put after - strip that out too
                // obligatory rant: DARNY DARN-DARN YOU Keyhole & google for bastardizing a format like this - someone should be kicked in the crotch for this!!!
                if (result.Contains(breaks))
                {
                    result = result.Substring(0, result.IndexOf(breaks));
                }
            }
            return result;
        }
    }

    public static class StringExtensions
    {
        public static string Right(this string givenString, int rightFromEnd)
        {
            return givenString.Substring(givenString.Length - rightFromEnd, rightFromEnd);
        }

        public static bool IsNumeric(this string givenString)
        {
            bool result = false;
            int thisInt = 0;
            try { thisInt = int.Parse(givenString); } catch { }
            if (thisInt != 0) result = true;
            else
            {
                if (givenString.Replace("0", "") != "") { result = true; } //this needs to be tested thoroguhly..
            }
            return result;
        }
        public static double ToDouble(this string givenString)
        {
            double result = 0;
            try { result = double.Parse(givenString); } catch { }
            return result;
        }

        public static decimal ToDecimal(this string givenString)
        {
            decimal result = 0;
            try { result = decimal.Parse(givenString); } catch { }
            return result;
        }
    }

}
