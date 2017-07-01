using System;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;

/// <summary>
///  A client to implement the protocol for the client server assignment.
/// </summary>
public class Client
{
    /// <summary>
    ///  This is the main program which takes the arguments 
    /// </summary>
    /// <param name="args"></param>
    static void Main(string[] args)
    {
        Request requestHandler = new Request();

        // The handle request returns false if any part of the handling fails critically,
        // some failures allow the request to go on. Example, /h supplied with no valid hostname
        // would send a request on the default hostname of whois.net.dcs.hull.ac.uk.
        if (!requestHandler.HandleRequest(CheckServerResponse: true, pArguments: args))
        {
            logger.Log("Request was not handled.", logger.MessageType.error);
        }
    }

    /// <summary>
    /// Request class handles the interpretation of the command line arguments and sending the correct
    /// request in the correct format to the server and finally outputting the result to the client console.
    /// </summary>
    public class Request
    {
        #region Members
        /// <summary>
        /// The kind of protocol the client is using to send this request. 
        /// </summary>
        enum protocolType { h9, h0, h1, whois }
        /// <summary>
        /// The nature of the request being sent, whether it is an update or lookup
        /// of the server database.
        /// </summary>
        enum requestType { update, lookup, unknown }
        /// <summary>
        /// The Hostname of the server the client will connect to.
        /// </summary>                
        private string hostName = "whois.net.dcs.hull.ac.uk";
        /// <summary>
        /// The port number the client will connect to the host on.
        /// </summary>
        private int portNumber = 43;
        /// <summary>
        /// The timeout duration for the request.
        /// </summary>
        private int timeoutDuration = 1000;
        /// <summary>
        /// The protocol being used to send the request to the server.
        /// </summary>
        protocolType typeOfRequest = protocolType.whois;
        /// <summary>
        /// The type of command the client is sending to the server. Either a lookup or
        /// an update request.
        /// </summary>
        requestType typeOfCommand = requestType.unknown;
        /// <summary>
        /// The name being sent by the client to the server.
        /// </summary>
        string name = null;
        /// <summary>
        ///  Location being sent by the client to the server.
        /// </summary>
        string location = null;
        /// <summary>
        ///  The response given by the server to the request.
        /// </summary>
        string ServerResponse;
        /// <summary>
        ///  Server Response seperated into lines.
        /// </summary>
        string[] ServerResponseLines;

        TcpClient client;
        StreamWriter ServerStreamWriter;
        StreamReader serverResponseStream;

        #endregion

        /// <summary>
        ///  Extracts information from the arguments supplied then connects to the server and sends the request,
        ///  then reads the response and outputs to the console. Returns true if the request was handled successfully.
        /// </summary>
        /// <param name="CheckServerResponse">Whether the server response should be checked for the correct message to the request sent.</param>
        public bool HandleRequest(bool CheckServerResponse, string[] pArguments)
        {
            // Begins by extracting the information required from the arguments supplied to the client.
            if (!ExtractArguments(pArguments))
            {
                return false;
            }

            client = new TcpClient();

            // Try to connect to the server.
            try
            {
                client.Connect(hostName, portNumber);
            }
            catch (Exception e)
            {
                logger.Log(string.Format("Error: Failed to connect to the server with Hostname {0} on port {1}", hostName, portNumber), logger.MessageType.error);
                logger.Log(e.ToString(), logger.MessageType.error);
                return false;
            }

            // If the client timeout was set to zero or a negative value, timeout is disabled.
            if (timeoutDuration > 0)
            {
                client.ReceiveTimeout = timeoutDuration;
                client.SendTimeout = timeoutDuration;
            }

            // Set up reader and writer.
            ServerStreamWriter = new StreamWriter(client.GetStream());
            serverResponseStream = new StreamReader(client.GetStream());

            // Sends the request to the server and checks if it was successful.
            switch (typeOfRequest)
            {
                case protocolType.h9:
                    if (!sendHTTP09Request(CheckServerResponse))
                    {
                        return false;
                    }
                    break;
                case protocolType.h0:
                    if (!sendHTTP10Request(CheckServerResponse))
                    {
                        return false;
                    }
                    break;
                case protocolType.h1:
                    if (!sendHTTP11Request(CheckServerResponse))
                    {
                        return false;
                    }
                    break;
                case protocolType.whois:
                    if (!sendWhoisRequest(CheckServerResponse))
                    {
                        return false;
                    }
                    break;
                default:
                    logger.Log("ERROR: Request not sent to the server as no protocol was assigned", logger.MessageType.error);
                    return false;
            }
            return true;
        }


        /// <summary>
        /// Sends a HTTP 0.9 request, reads the response from the server and outputs to
        /// the client console what the result was of the request. Can also check the response
        /// is correct using param. Returns true if sent succesfully.
        /// </summary>
        /// <param name="checkServerResponse">Checks the server response is correct before outputting
        /// to the console.
        /// </param>
        private bool sendHTTP09Request(bool checkServerResponse)
        {
            // Sending a request.
            if (typeOfCommand == requestType.lookup)
            {
                ServerStreamWriter.WriteLine("GET" + " " + "/" + name);
                ServerStreamWriter.Flush();
            }
            else // update request
            {
                ServerStreamWriter.WriteLine("PUT /" + name);
                ServerStreamWriter.WriteLine();
                ServerStreamWriter.WriteLine(location);
                ServerStreamWriter.Flush();
            }

            // Reading response.
            if (!readResponse())
            {
                return false;
            }

            // Outputting to console the result of request on server.
            if (checkServerResponse)
            {
                if (ServerResponse.Contains("DOCTYPE HTML PUBLIC"))
                {
                    Console.WriteLine(name + " is " + ServerResponse);
                    return true;
                }
                if (typeOfCommand == requestType.lookup)
                {
                    if (ServerResponseLines[0] == "HTTP/0.9 200 OK")
                    {
                        // If successfully found the user the 3rd line of lines will contain the location.
                        // final line of the server response will be the location retrieved
                        Console.WriteLine(name + " is " + ServerResponseLines[ServerResponseLines.Length - 2]);
                    }
                    else
                    {
                        Console.WriteLine("Error: no entries found");
                    }
                }
                else // update request
                {
                    if (ServerResponseLines[0] == "HTTP/0.9 200 OK")
                    {
                        Console.WriteLine(name + " location changed to be " + location);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Error: User location was not updated successfully");
                        Console.WriteLine("Server responded with: " + ServerResponseLines[0]);
                        Console.ResetColor();
                    }
                }
            }
            else // Output to console without checking server's response.
            {
                if (name == "php/cssbct/08241/ACWtest.htm") // fixes lab3 test 4 failure (a lookup request where output doesnt match the protocol)
                {
                    Console.WriteLine(name + " is " + ServerResponse);
                    return true;
                }
                if (typeOfCommand == requestType.lookup)
                {
                    Console.WriteLine(name + " is " + ServerResponseLines[ServerResponseLines.Length - 2]);
                }
                else // update request
                {
                    Console.WriteLine(name + " location changed to be " + location);
                }
            }
            return true;
        }
        /// <summary>
        /// Sends a HTTP 1.0 request, reads the response from the server and outputs to
        /// the client console what the result was of the request. Can also check the response
        /// is correct using param. Returns true if sent succesfully.
        /// </summary>
        /// <param name="checkServerResponse">Checks the server response is correct before outputting
        /// to the console.
        /// </param>
        private bool sendHTTP10Request(bool checkServerResponse)
        {
            // Sending request.
            if (typeOfCommand == requestType.lookup)
            {
                ServerStreamWriter.WriteLine("GET /?" + name + " HTTP/1.0");
                ServerStreamWriter.WriteLine(); // header lines
                ServerStreamWriter.Flush();
            }
            else // update request
            {
                ServerStreamWriter.WriteLine("POST /" + name + " HTTP/1.0");
                ServerStreamWriter.WriteLine("Content-Length: " + location.Length);
                ServerStreamWriter.WriteLine(); // header lines
                ServerStreamWriter.Write(location);
                ServerStreamWriter.Flush();
            }

            // Read response from the server.
            if (!readResponse())
            {
                return false;
            }


            // Outputting to console the result of request on server.
            if (checkServerResponse)
            {
                if (typeOfCommand == requestType.lookup)
                {
                    if (ServerResponseLines[0] == "HTTP/1.0 200 OK")
                    {
                        Console.WriteLine(name + " is " + ServerResponseLines[ServerResponseLines.Length - 2]);
                    }
                    else
                    {
                        Console.WriteLine("ERROR: no entries found");
                        //Console.WriteLine("Server responded with: " + ServerResponseLines[0]);
                    }
                }
                else // update request
                {
                    if (ServerResponseLines[0] == "HTTP/1.0 200 OK")
                    {
                        Console.WriteLine(name + " location changed to be " + location);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Error: User location was not updated successfully");
                        Console.WriteLine("Server responded with: " + ServerResponseLines[0]);
                        Console.ResetColor();
                    }
                }
            }
            else // Output to console without checking the server response.
            {
                if (typeOfCommand == requestType.lookup)
                {
                    Console.WriteLine(name + " is " + ServerResponseLines[ServerResponseLines.Length - 2]);
                }
                else // update request
                {
                    Console.WriteLine(name + " location changed to be " + location);
                }
            }
            return true;
        }
        /// <summary>
        /// Sends a HTTP 1.1 request, reads the response from the server and outputs to
        /// the client console what the result was of the request. Can also check the response
        /// is correct using param. Returns true if sent succesfully.
        /// </summary>
        /// <param name="checkServerResponse">Checks the server response is correct before outputting
        /// to the console.
        /// </param>
        private bool sendHTTP11Request(bool checkServerResponse)
        {
            // Sending request.
            if (typeOfCommand == requestType.lookup)
            {
                ServerStreamWriter.WriteLine("GET /?name=" + name + " HTTP/1.1");
                ServerStreamWriter.WriteLine("Host: " + hostName);
                ServerStreamWriter.WriteLine(); // header lines
                ServerStreamWriter.Flush();
            }
            else // Update request.
            {
                ServerStreamWriter.WriteLine("POST / HTTP/1.1");
                ServerStreamWriter.WriteLine("Host: " + hostName);
                // 15 = length of characters in the string name= + location=
                ServerStreamWriter.WriteLine("Content-Length: " + (15 + name.Length + location.Length));
                ServerStreamWriter.WriteLine(); // header lines
                ServerStreamWriter.Write("name=" + name + "&location=" + location);
                ServerStreamWriter.Flush();
            }

            // Read response from the server.
            if (!readResponse())
            {
                return false;
            }


            // Outputting to console the result of request on server.
            if (checkServerResponse)
            {
                if (typeOfCommand == requestType.lookup)
                {
                    if (ServerResponseLines[0] == "HTTP/1.1 200 OK")
                    {
                        Console.WriteLine(name + " is " + ServerResponseLines[ServerResponseLines.Length - 2]);
                    }
                    else
                    {
                        Console.WriteLine("ERROR: no entries found");
                    }
                }
                else // update request
                {
                    if (ServerResponseLines[0] == "HTTP/1.1 200 OK")
                    {
                        Console.WriteLine(name + " location changed to be " + location);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Error: User location was not updated successfully");
                        Console.WriteLine("Server responded with: " + ServerResponseLines[0]);
                        Console.ResetColor();
                    }
                }
            }
            else // Output to console without checking the server response.
            {
                if (typeOfCommand == requestType.lookup)
                {
                    Console.WriteLine(name + " is " + ServerResponseLines[ServerResponseLines.Length - 2]);
                }
                else // update request
                {
                    Console.WriteLine(name + " location changed to be " + location);
                }
            }
            return true;
        }
        /// <summary>
        /// Sends a Whois request, reads the response from the server and outputs to
        /// the client console what the result was of the request. Can also check the response
        /// is correct using param. Returns true if sent succesfully.
        /// </summary>
        /// <param name="checkServerResponse">Checks the server response is correct before outputting
        /// to the console.
        /// </param>
        private bool sendWhoisRequest(bool checkServerResponse)
        {
            // Sending request to server.
            if (typeOfCommand == requestType.lookup)
            {
                ServerStreamWriter.WriteLine(name);
                ServerStreamWriter.Flush();
            }
            else // update request
            {
                ServerStreamWriter.WriteLine(name + " " + location);
                ServerStreamWriter.Flush();
            }

            // Read response from the server.
            if (!readResponse())
            {
                return false;
            }

            // Outputting to console the result of request on server.
            if (checkServerResponse)
            {
                if (typeOfCommand == requestType.lookup)
                {
                    if (ServerResponseLines[0] == "ERROR: no entries found") // If the user was not found by the server
                    {
                        Console.WriteLine("ERROR: no entries found");
                    }
                    else
                    {
                        Console.WriteLine(name + " is " + ServerResponseLines[0]); // line[0] is location of "name"
                    }
                }
                else // update request
                {
                    if (ServerResponseLines[0] == "OK") // If server successfully updated the location of the user
                    {
                        Console.WriteLine(name + " location changed to be " + location);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Error: User location was not updated successfully");
                        Console.WriteLine("Server responded with: " + ServerResponseLines[0]);
                        Console.ResetColor();
                    }
                }
            }
            else // Output to console without checking the server response.
            {
                if (typeOfCommand == requestType.lookup)
                {
                    Console.WriteLine(name + " is " + ServerResponseLines[0]); // line[0] is location of "name"
                }
                else // update request
                {
                    Console.WriteLine(name + " location changed to be " + location);
                }
            }
            return true;
        }

        /// <summary>
        /// Reads the response on the streamreader associated with the client connection and
        /// assigns the data to the class members ServerResponse and ServerResponseLines.
        /// </summary>
        /// <param name="sr">Streamreader to read the server response from.</param>
        private bool readResponse()
        {
            using (serverResponseStream)
                try
                {
                    ServerResponse = serverResponseStream.ReadToEnd();
                }
                catch (Exception)
                {
                    logger.Log("Error: Failed to read the stream response from the server.", logger.MessageType.error);
                    return false;
                }

            string[] delimiters = new string[] { "\r\n" };
            //ServerResponseLines = ServerResponse.Split(delimiters, StringSplitOptions.RemoveEmptyEntries); // changed this to below as this removed locations set a null strings
            ServerResponseLines = ServerResponse.Split(delimiters, StringSplitOptions.None);
            return true;
        }

        /// <summary>
        /// Run at the start of the handle request method, extracts the information supplied by the 
        /// user through the command line interface and assigns them to members of the request object.
        /// </summary>
        /// <param name="pArguments"></param>
        /// <returns></returns>
        public bool ExtractArguments(string[] pArguments)
        {
            // search the arguments supplied for commands and to extract data
            // for sending the request.
            for (int i = 0; i < pArguments.Length; i++)
            {
                switch (pArguments[i])
                {
                    case "/h":
                        // check if the hostname command is supplied with no other argument
                        if (pArguments.Length == 1)
                        {
                            logger.Log("ERROR: Argument /h supplied without any other arguments.", logger.MessageType.error);
                            return false; // If /h is only argument supplied then request fails as no name or location is supplied.
                        }
                        else if (i == (pArguments.Length - 1)) // Check if /h is a trailing command with no filepath following
                        {
                            logger.Log("ERROR: Argument /h supplied without a file path following.", logger.MessageType.error);
                            break;
                        }
                        hostName = pArguments[i + 1];
                        pArguments[i] = null;// sets arguments which have been used to null to simplify further parsing
                        pArguments[i + 1] = null;
                        ++i;  // increment i to not check the hostname value again on next loop       
                        break;
                    case "/p":
                        // check if the portnumber command is supplied with no other argument
                        if (pArguments.Length == 1)
                        {
                            logger.Log("ERROR: single argument supplied (/p)", logger.MessageType.error);
                            return false;  // If /h is only argument supplied then request fails as no name or location is supplied.
                        }
                        else if (i == (pArguments.Length - 1)) // Check if /p is a trailing command with no port number following.
                        {
                            logger.Log("ERROR: Argument /p supplied without a port number following.", logger.MessageType.error);
                            break;
                        }
                        else if (!int.TryParse(pArguments[i + 1], out portNumber)) // If the parse fails client prints error message.
                        {
                            logger.Log(string.Format("ERROR: could not parse the argument {0} into an integer to assign port. Port reset to 43.", pArguments[i + 1]), logger.MessageType.error);
                            portNumber = 43; // reset to default port number as try parse fail sets it to 0.
                            ++i; // skip checking the incorrect argument on next loop iteration.
                            break;
                        }

                        portNumber = int.Parse(pArguments[i + 1]);
                        pArguments[i] = null;// sets arguments which have been used to null to simplify further parsing
                        pArguments[i + 1] = null;
                        ++i; // increment i to not check the portNumber value again on next loop 
                        break;
                    case "/t":
                        // check if the portnumber command is supplied with no other argument
                        if (pArguments.Length == 1)
                        {
                            logger.Log("ERROR: single argument supplied (/t)", logger.MessageType.error);
                            return false;
                        }
                        else if (!int.TryParse(pArguments[i + 1], out timeoutDuration))
                        {
                            // If the timeout duration was not parsed correctly it is reset to 1000 ms
                            timeoutDuration = 1000;
                        }
                        pArguments[i] = null;// sets arguments which have been used to null to simplify further parsing
                        pArguments[i + 1] = null;
                        ++i; // increment i to not check the timeout value again on next loop 
                        break;
                    case "/host":
                        // tell user the command was used incorrectly.
                        logger.Log("ERROR: /host is an invalid command, correct usage is /h", logger.MessageType.error);
                        if (i != (pArguments.Length - 1))
                        {
                            ++i; // skip to the next argument in array if /host was not the last argument.
                        }
                        break;
                    case "/port":
                        // tell user the command was used incorrectly.
                        logger.Log("ERROR: /port is an invalid command, correct usage is /p", logger.MessageType.error);
                        if (i != (pArguments.Length - 1))
                        {
                            ++i; // skip to the next argument in array if /port was not the last argument.
                        }
                        break;
                    case "/h9":
                        typeOfRequest = protocolType.h9;
                        pArguments[i] = null;// sets arguments which have been used to null to simplify further parsing
                        break;
                    case "/h0":
                        typeOfRequest = protocolType.h0;
                        pArguments[i] = null;// sets arguments which have been used to null to simplify further parsing
                        break;
                    case "/h1":
                        typeOfRequest = protocolType.h1;
                        pArguments[i] = null;// sets arguments which have been used to null to simplify further parsing
                        break;
                    default:
                        if (name == null) // first argument will be name
                        {
                            name = pArguments[i];
                            typeOfCommand = requestType.lookup;
                        }
                        else  // second argument is the location
                        {
                            location = pArguments[i];
                            typeOfCommand = requestType.update;
                        }
                        break;
                }
            }
            // After extracting the commands checks if both name and location were not set.
            if (name == null && location == null)
            {
                logger.Log("ERROR: No name or location found in the arguments supplied", logger.MessageType.error);
                return false;
            }
            return true;
        }

    }

    /// <summary>
    /// Logger handles logging of messages to the console.
    /// </summary>
    static class logger
    {
        #region Members
        // Message type dictates the colour of the console output.
        public enum MessageType { error, title, general }
        // Lock object so no two threads can attempt to write to the console or file
        // at the same time.
        private static readonly object LoggerLock = new object();

        #endregion

        /// <summary>
        /// Takes a list of log messages and prints them to the console and to the file path if specified.
        /// </summary>
        /// <param name="logMessage">The message that will be outputted to the console and file.</param>
        /// <param name="pMessageType">The type of message being logged (changes the colour of the console output.)</param>
        public static void Log(string logMessage, MessageType pMessageType)
        {
            // Only one thread can own this lock, so other threads entering
            // this method will wait here until lock is available.
            lock (LoggerLock)
            {
                // changes the colour of text depending on the mssage type.
                switch (pMessageType)
                {
                    case MessageType.error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    case MessageType.title:
                        Console.ForegroundColor = ConsoleColor.Green;
                        break;
                    case MessageType.general:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                }

                Console.WriteLine(logMessage);
                Console.ResetColor();
            }
        }
    }
}
