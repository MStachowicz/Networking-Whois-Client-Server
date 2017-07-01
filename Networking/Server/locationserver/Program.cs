using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using System.Threading;

public class locationserver
{
    /// <summary>
    /// This is the main program for a console application that is invoked with an
    /// array of strings that were received on the command line.
    /// </summary>
    /// <param name="args"></param>
    static void Main(string[] args)
    {
        // Starts the server and passes the arguments to extract any commands sent.
        runServer(args);
    }

    /// <summary>
    /// Number of threads currently being used by the server to handle requests. 
    /// Incremented in the Handler class constructor and decremented in its descructor 
    /// as a handler object is created to run on each new thread.
    /// </summary>
    static int threadCount = 0;

    /// <summary>
    /// This method is called by the main program to create and run a server
    /// it is in a seperate method to facilitate threading.
    /// </summary>
    static void runServer(string[] arguments)
    {
        #region loop through arguments for commands

        // Loop through arguments supplied to the server, searching for commands /l providing a 
        // log file path and /f for a database loading/saving filepath.
        for (int i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "/l":
                    if (arguments.Length == 1)
                    {
                        Console.WriteLine("ERROR: Argument /l supplied without any other arguments.");
                        break;
                    }
                    else if (arguments[i + 1] == null)
                    {
                        Console.WriteLine("ERROR: Argument /l supplied without a filepath following.");
                        break;
                    }
                    else if (!logger.setFilePath(arguments[i + 1]))
                    {
                        logger.Log("ERROR: you do not have permissions to write to the directory: " + arguments[i + 1], logger.MessageType.error);
                        ++i;
                        break;
                    }
                    else
                    {
                        logger.Log("Log file will be stored in " + logger.getFilePath(), logger.MessageType.general);
                        ++i;
                        break;
                    }
                case "/f":
                    if (arguments.Length == 1)
                    {
                        Console.WriteLine("ERROR: Argument /f supplied without any other arguments.");
                        break;
                    }
                    else if (arguments[i + 1] == null)
                    {
                        Console.WriteLine("ERROR: Argument /f supplied without a filepath following.");
                        break;
                    }
                    else if (logger.getFilePath() != null && (arguments[i + 1] == logger.getFilePath())) // Checks if user attempts to write to the same directory as the logger file.
                    {
                        logger.Log("ERROR: cannot use the same directory for the database as the log file", logger.MessageType.error);
                        ++i;
                        break;
                    }
                    else if (!Database.setFilePath(arguments[i + 1]))
                    {
                        logger.Log("ERROR: you do not have permissions to write to the directory: " + arguments[i + 1], logger.MessageType.error);
                        ++i;
                        break;
                    }
                    else
                    {
                        logger.Log("Database file will be stored in " + Database.getFilePath(), logger.MessageType.general);
                        ++i;
                        break;
                    }
                default:
                    Console.WriteLine("ERROR: Could not identify the commmand in the argument array: " + arguments[i]);
                    break;
            }
        }
        #endregion

        TcpListener listener;
        Socket connection;
        Handler RequestHandler;

        try
        {
            // Create a TCP socket to listen on port 43 for incoming requests.
            // and start listening.
            listener = new TcpListener(IPAddress.Any, 43);
            listener.Start();

            logger.Log("Server started listening", logger.MessageType.title);

            // Loop forever handling all incoming requests by creating threads.
            while (true)
            {
                // When a request is received create a socket to handle it and 
                // invoke doRequest on a new thread to handle the details.
                connection = listener.AcceptSocket();
                RequestHandler = new Handler();
                Thread t = new Thread(() => RequestHandler.doRequest(connection));
                t.Start();
            }
        }
        catch (Exception e)
        {
            // If there was an error in processing - catch and log the details.
            logger.Log("Exception " + e.ToString(), logger.MessageType.error);
        }
    }

    /// <summary>
    /// Handler class 
    /// </summary>
    class Handler
    {
        /// <summary>
        /// The default constructor will increment the thread count.
        /// </summary>
        public Handler()
        {
            ++threadCount;
        }

        // Enums to allow server to identify the type of request and protocol client is using
        // and reply in the correct format with knowing these.
        /// <summary>
        /// The protocol the 
        /// </summary>
        enum protocol { h9, h0, h1, whois, unknown }
        enum request { update, lookup, unknown }
        // List to contain all the log messages which will be printed when the request is finished being handled.
        List<string> RequestLog = new List<string>();




        /// <summary>
        /// The type of client connecting to this server.
        /// </summary>
        public enum ClientType
        {
            Game, locationClient
        };
        /// <summary>
        /// Sets the method of interpretation of messages in doRequest method.
        /// </summary>
        public static ClientType interpretationMethod = ClientType.Game; // game by default for ACW 2

        public IPAddress MasterPeerIP;
        public IPAddress SlavePeerIP;


        /// <summary>
        /// This method is called after the server receives a connection on a listener.
        /// It processes the lines received as a request in the desired protocol and
        /// sends back appropriate reply to the client.
        /// </summary>
        /// <param name="socketStream"></param>
        public void doRequest(Socket connection)
        {
            NetworkStream socketStream;
            socketStream = new NetworkStream(connection);
            RequestLog.Add(string.Format("Connection received \r\n\tTime: {0}\r\n\tThread: {1}\r\n\tIP Address: {2}", DateTime.Now.ToString(), threadCount.ToString(), connection.LocalEndPoint.ToString()));

            try
            {
                #region Read request sent by client
                // Set the timeout value to 1 second
                int timeoutduration = 1000;
                socketStream.ReadTimeout = timeoutduration;
                socketStream.WriteTimeout = timeoutduration;

                // create some stream readers to handle the socket I/O
                // -Much more convenient than byte arrays
                // Particularly as the data will be ASCI text structures in lines.
                StreamReader sr = new StreamReader(socketStream);
                StreamWriter sw = new StreamWriter(socketStream);

                // Reads the stream into a string clientMessage until the end of file.
                string clientMessage = readStream(sr);
                RequestLog.Add(string.Format("Client sent:\r\n\t{0}", clientMessage));

                // Splits the client message by lines to more easily extract information.
                string[] delimiters = new string[] { "\r\n" };
                string[] lines = clientMessage.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

                #endregion



                if (interpretationMethod == ClientType.locationClient)
                {
                    #region  Interpret the request sent by the client
                    // 4 pieces of information are extracted from the request.
                    protocol protocolUsed = protocol.unknown;
                    request requestUsed = request.unknown;
                    string name = null;
                    string location = null;

                    // Conditionals check the client message for strings which reveal what protocol and 
                    // type of request the client sent.
                    if (clientMessage.Contains("HTTP/1.0"))
                    {
                        protocolUsed = protocol.h0;

                        if (clientMessage.StartsWith("GET"))
                        {
                            requestUsed = request.lookup;
                            name = findWord(lines[0], "/?", " ");
                        }
                        else if (clientMessage.StartsWith("POST"))
                        {
                            requestUsed = request.update;
                            name = findWord(lines[0], "/", " ");
                            location = lines[lines.Length - 1];
                        }
                    }
                    else if (clientMessage.Contains("HTTP/1.1"))
                    {
                        protocolUsed = protocol.h1;

                        if (clientMessage.StartsWith("GET"))
                        {
                            requestUsed = request.lookup;

                            name = findWord(lines[0], "/?name=", " ");
                        }
                        else if (clientMessage.StartsWith("POST"))
                        {
                            requestUsed = request.update;
                            name = findWord(lines[lines.Length - 1], "name=", "&location");
                            location = lines[lines.Length - 1].Substring(15 + name.Length);
                        }
                    }
                    else // If the protocol type not in request must be either HTTP 0.9 or Whosis protocol
                    {
                        if (clientMessage.StartsWith("GET /") || clientMessage.StartsWith("PUT /"))
                        {
                            protocolUsed = protocol.h9;
                            if (clientMessage.StartsWith("GET"))
                            {
                                requestUsed = request.lookup;
                                name = findWord(clientMessage, "/", "\r\n");
                            }
                            else
                            {
                                requestUsed = request.update;
                                name = lines[0].Substring(5); // 5 skips over "PUT /"
                                location = lines[lines.Length - 1];
                            }
                        }
                        else
                        {
                            protocolUsed = protocol.whois;
                            if (clientMessage.Contains(" "))
                            {
                                requestUsed = request.update;
                                name = clientMessage.Substring(0, clientMessage.IndexOf(" "));
                                location = clientMessage.Substring(name.Length).Trim();
                            }
                            else
                            {
                                requestUsed = request.lookup;
                                name = clientMessage.Trim();
                            }
                        }
                    }
                    // Logs the information extracted from the client message.
                    RequestLog.Add(string.Format("Extracted data from the client message:\r\n\tName: {0}\r\n\tlocation: {1}\r\n\tProtocol: {2}\r\n\tRequest type: {3}",
                        name,
                        location,
                        protocolUsed,
                        requestUsed));
                    #endregion
                    #region server response to client request

                    // Server responds to the request sent by the client based on the protocol used and type of request.
                    switch (protocolUsed)
                    {
                        case protocol.whois:
                            if (requestUsed == request.lookup)
                            {
                                if (Database.isPersonInDatabase(name))
                                {
                                    string reply = Database.getLocation(name);
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found an entry for the whois lookup of name \"{0}\"\r\nServer replied with:\r\n\t{0}", reply));
                                    break;
                                }
                                else // Person not found in the database
                                {
                                    string reply = "ERROR: no entries found";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found no entry for the whois lookup of name \"{0}\"\r\nServer replied with:\r\n{1}", name, reply));
                                    break;
                                }
                            }
                            else // Update request
                            {
                                if (Database.isPersonInDatabase(name)) // If user is in the database
                                {
                                    // Update their location
                                    Database.setLocation(name, location);
                                    sw.WriteLine("OK");
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found the user \"{0}\" in the database and updated their location to {1}\r\nServer replied with:\r\n\tOK", name, location));
                                    break;
                                }
                                else // If not found in the databse
                                {
                                    // Add the user name and location to database
                                    Database.add(name, location);
                                    sw.WriteLine("OK");
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server did not find the user \"{0}\" in the database and added them with location set to {1}\r\nServer replied with:\r\n\tOK", name, location));
                                    break;
                                }

                            }
                        case protocol.h9:
                            if (requestUsed == request.lookup)
                            {
                                if (Database.isPersonInDatabase(name))
                                {
                                    string reply = "HTTP/0.9 200 OK\r\nContent-Type: text/plain\r\n\r\n";
                                    sw.WriteLine(reply + Database.getLocation(name));
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found an entry for the HTTP 0.9 lookup of name \"{0}\"\r\nServer replied with:\r\n\t{1}", name, reply));
                                    break;
                                }
                                else
                                {
                                    string reply = "HTTP/0.9 404 Not Found\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found no entry for the HTTP 0.9 lookup of name \"{0}\"\r\nServer replied with:\r\n\t{1}", name, reply));
                                    break;
                                }
                            }
                            else // Update request
                            {
                                if (Database.isPersonInDatabase(name)) // If user is in the database
                                {
                                    // Update their location
                                    Database.setLocation(name, location);
                                    string reply = "HTTP/0.9 200 OK\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found the user \"{0}\" in the database and updated their location to {1}\r\nServer replied with:\r\n\t{2}", name, location, reply));
                                    break;
                                }
                                else // If not found in the databse
                                {
                                    // add the user name and location to databse
                                    Database.add(name, location);
                                    string reply = "HTTP/0.9 200 OK\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server did not find the user \"{0}\" in the database and added them with location set to {1}\r\nServer replied with:\r\n\t{2}", name, location, reply));
                                    break;
                                }

                            }
                        case protocol.h0:
                            if (requestUsed == request.lookup)
                            {
                                if (Database.isPersonInDatabase(name))
                                {
                                    string reply = "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\n\r\n" + Database.getLocation(name);
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found an entry for the HTTP 1.0 lookup of name \"{0}\"\r\nServer replied with:\r\n\t{1}", name, reply));
                                    break;
                                }
                                else
                                {
                                    string reply = "HTTP/1.0 404 Not Found\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found no entry for the HTTP 1.0 lookup of name \"{0}\"\r\nServer replied with:\r\n\t{1}", name, reply));
                                    break;
                                }
                            }
                            else // Update request
                            {
                                if (Database.isPersonInDatabase(name)) // If user is in the database
                                {
                                    // Update their location
                                    Database.setLocation(name, location);
                                    string reply = "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found the user \"{0}\" in the database and updated their location to {1}\r\nServer replied with:\r\n\t{2}", name, location, reply));
                                    break;
                                }
                                else // If not found in the databse
                                {
                                    // add the user name and location to databse
                                    Database.add(name, location);
                                    string reply = "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server did not find the user \"{0}\" in the database and added them with location set to {1}\r\nServer replied with:\r\n\t{2}", name, location, reply));
                                    break;
                                }
                            }
                        case protocol.h1:
                            if (requestUsed == request.lookup)
                            {
                                if (Database.isPersonInDatabase(name))
                                {
                                    string reply = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\n" + Database.getLocation(name);
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found an entry for the HTTP 1.1 lookup of name \"{0}\"\r\nServer replied with:\r\n\t{1}", name, reply));
                                    break;
                                }
                                else // User not found in the database
                                {
                                    string reply = "HTTP/1.1 404 Not Found\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found no entry for the HTTP 1.1 lookup of name \"{0}\"\r\nServer replied with:\r\n\t{1}", name, reply));
                                    break;
                                }
                            }
                            else // HTTP 1.1 Update request
                            {
                                if (Database.isPersonInDatabase(name)) // If user is in the database
                                {
                                    // Update their location
                                    Database.setLocation(name, location);
                                    string reply = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server found the user \"{0}\" in the database and updated their location to {1}\r\nServer replied with:\r\n\t{2}", name, location, reply));
                                    break;
                                }
                                else // If not found in the databse
                                {
                                    // add the user name and location to databse
                                    Database.add(name, location);
                                    string reply = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n";
                                    sw.WriteLine(reply);
                                    sw.Flush();
                                    RequestLog.Add(string.Format("Server did not find the user \"{0}\" in the database and added them with location set to {1}\r\nServer replied with:\r\n\t{2}", name, location, reply));
                                    break;
                                }
                            }
                        default:
                            logger.Log("ERROR: failure to assign a protocol type to the client request for sending a response",
                                logger.MessageType.error);
                            break;
                    }
                    #endregion
                }
                else if (interpretationMethod == ClientType.Game)
                {

                    // Splits the client message by lines to more easily extract information.
                    delimiters = new string[] { "@" };
                    lines = clientMessage.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);


                    #region Interpreting message for game

                    string reply = "Game message not interpreted correctly";

                    if (lines[lines.Length - 1] == "MasterPeer") // if last term is master peer then set the ip as master peer.
                    {
                        MasterPeerIP = IPAddress.Parse(lines[0]);
                        SlavePeerIP = IPAddress.Parse(lines[1]);
                        reply = string.Format("serverSet@MasterPeerIP:{0}@SlavePeerIP:{1}", MasterPeerIP, SlavePeerIP);
                    }
                    else if (lines[lines.Length - 1] == "MasterPeer")
                    {

                    }
                    else if (clientMessage.StartsWith("GameReset"))
                    {
                    }
                    else if (clientMessage.StartsWith("UpdateHighScore")) // meesage sent by clients to update the highscore table in the server.
                    {

                        string HighScore = null;
                        // Builds the string to contain the highscore table in and use to set the location in the database.
                        for (int i = 1; i < lines.Length; i++) // start from 1 and skip the UpdateHighScore message.
                        {
                            HighScore += lines[i] + '@';
                        }

                        HighScore = HighScore.Remove(HighScore.Length - 1);


                        if (Database.isPersonInDatabase("Highscore"))
                        {                       
                            Database.setLocation("Highscore", HighScore);
                            reply = "location set";
                            RequestLog.Add(string.Format("server received request to update highscrore table."));
                            RequestLog.Add(string.Format("server set the highscore table to: " + HighScore));
                        }
                        else // if highscore table is not in the database it is added
                        {
                            // Add the user name and location to database
                            Database.add("Highscore", HighScore); // dafault highscore set
                            reply = "server received request to update highscrore table.";
                            RequestLog.Add("server received request to update highscrore table. No entry found, created highscore entry with table received.");
                        }
                    }
                    else if (clientMessage.StartsWith("RetrieveHighScore")) // Message received when a client attempts to retrieve the highscore table.
                    {
                        if (Database.isPersonInDatabase("Highscore"))
                        {
                            reply = "HighscoreFound@" + Database.getLocation("Highscore"); // location contains the highscores stored.
                            RequestLog.Add(string.Format("server received request to retrieve highscrore and returned request."));
                        }
                        else // Highscore not found in the dictionary creates a new default entry
                        {
                            // Add the user name and location to database
                            Database.add("Highscore", "Darren@5@Dawn@4@David@3@Steven@2@Susan@1"); // dafault highscore set
                            reply = "NoHighscoreFound@Darren@5@Dawn@4@David@3@Steven@2@Susan@1";
                            RequestLog.Add("Server did not find an entry for highscore table and created the default.");
                        }
                    }
                    else if (clientMessage == "connected")
                    {
                        if (MasterPeerIP != null)
                        {
                            reply = "startGameSlave";
                        }
                        else
                        {
                            reply = "MasterPeerNotConnected";
                        }
                    }
                    else
                    {
                        logger.Log("Network Manager could not interpret message:  " + clientMessage + " From IP: " + connection.LocalEndPoint,
                            logger.MessageType.error);
                    }

                    sw.WriteLine(reply);
                    sw.Flush();
                    RequestLog.Add(string.Format("Server replied to game message. \r\nServer replied with:\r\n\"{0}\"", reply));

                    #endregion
                }

            }
            catch (Exception e)
            {
                logger.Log(string.Format("Uncaught exception in DoRequest: {0}", e.ToString()),
                    logger.MessageType.error);
            }
            finally
            {
                socketStream.Close();
                connection.Close();

                // At the end of a request the RequestLog list will be outputted to the console and flushed to a file if specified.
                RequestLog.Add("Connection closed\r\n\tTime: " + DateTime.Now.ToString());
                logger.Log(RequestLog);

                // The thread still exists within this method but no operations will interfere with
                // database save as this is the only one running.
                threadCount--;
                // When no requests are being handled the server is saved and no 
                // new requests will occur concurrently.
                if (threadCount == 0 && Database.savingToFileEnabled)
                {
                    Database.saveDatabase();
                }
            }
        }

        /// <summary>
        /// Reads the stream into a string char by char until the end of the file.
        /// </summary>
        /// <param name="reader"></param>
        /// Reader to read data from.
        /// <returns></returns>
        public static string readStream(StreamReader reader)
        {
            char[] buffer = new char[1];
            string output = "";
            while (reader.Peek() > -1)
            {
                reader.Read(buffer, 0, 1);
                output += buffer[0];
            }
            return output;
        }

        /// <summary>
        /// Exctracts a string from an input string between the two strings supplied as parameters.
        /// </summary>
        /// <param name="input">String to cut the string out from</param>
        /// <param name="startString">String to begin cut from</param>
        /// <param name="endString">String to end cut on</param>
        /// <returns></returns>
        public static string findWord(string input, string startString, string endString)
        {
            string result;

            int pFrom = input.IndexOf(startString) + startString.Length;
            int pTo = input.LastIndexOf(endString);

            return result = input.Substring(pFrom, pTo - pFrom);
        }
    }

    /// <summary>
    /// Logger handles logging of messages to both the console and a file if a filepath is specified.
    /// </summary>
    static class logger
    {
        #region Members
        // Message type dictates the colour of the console output.
        public enum MessageType { error, title, general }
        // Path where the log file is kept.
        private static string LogFilePath;
        // If no log file path is supplied logger will log to console only.
        private static bool logToFileEnabled = false;
        // Streamwriter which will write to a file.
        private static StreamWriter fileStreamWriter;
        // Lock object so no two threads can attempt to write to the console or file
        // at the same time.
        private static readonly object LoggerLock = new object();

        // strings used to seperate a normal log message and a log list.
        private const string logListSeperator = "*********************************************************************************";
        private const string logSeperator = "---------------------------------------------------------------------------";
        #endregion

        #region Methods

        /// <summary>
        /// Sets the file path of the logger and enables logging to file. Checks if user has permission to 
        /// create files in the directory. If user doesnt have permission returns as false.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static bool setFilePath(string pFilePath)
        {
            try
            {
                using (File.Open(pFilePath, FileMode.OpenOrCreate)) { }
            }
            catch (Exception)
            {
                return false;
            }

            LogFilePath = pFilePath;
            logToFileEnabled = true;
            return true;
        }

        /// <summary>
        /// Returns the file path of the log file.
        /// </summary>
        public static string getFilePath()
        {
            return LogFilePath;
        }

        /// <summary>
        /// Takes a list of logs and prints them in sequence allowing a single request performed
        /// to remain readable within the file and console when using threading.
        /// </summary>
        /// <param name="pLogList">string list containing all the log messages created during the request.</param>
        public static void Log(List<string> pLogList)
        {
            // Only one thread can own this lock, so other threads entering
            // this method will wait here until lock is available.
            lock (LoggerLock)
            {
                // Console log.
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(logListSeperator);
                foreach (string i in pLogList)
                {
                    Console.WriteLine(i);
                    Console.WriteLine(logSeperator);
                }
                Console.WriteLine(logListSeperator);
                Console.ResetColor();


                if (logToFileEnabled)
                {
                    // If the log file exists append to it.
                    if (File.Exists(LogFilePath))
                    {
                        using (fileStreamWriter = File.AppendText(LogFilePath))
                        {
                            fileStreamWriter.WriteLine(logListSeperator);
                            foreach (string i in pLogList)
                            {
                                fileStreamWriter.WriteLine(i);
                                fileStreamWriter.WriteLine(logSeperator);
                            }
                            fileStreamWriter.WriteLine(logListSeperator);
                        }
                    }
                    else // otherwise the file is created.
                    {
                        using (fileStreamWriter = File.CreateText(LogFilePath))
                        {
                            fileStreamWriter.WriteLine(logListSeperator);
                            foreach (string i in pLogList)
                            {
                                fileStreamWriter.WriteLine(i);
                                fileStreamWriter.WriteLine(logSeperator);
                            }
                            fileStreamWriter.WriteLine(logListSeperator);
                        }
                    }
                }
            }
        }

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
                Console.WriteLine("---------------------------------------------------------------------------");
                Console.ResetColor();

                if (logToFileEnabled)
                {
                    // if the log file exists it is appended to.
                    if (File.Exists(LogFilePath))
                    {
                        using (fileStreamWriter = File.AppendText(LogFilePath))
                        {
                            fileStreamWriter.WriteLine(logMessage);
                            fileStreamWriter.WriteLine("---------------------------------------------------------------------------");
                        }
                    }
                    // otherwise the file is created in the directory LogFilePath.
                    else
                    {


                        // Create a file to write to.
                        using (fileStreamWriter = File.CreateText(LogFilePath))
                        {
                            fileStreamWriter.WriteLine(logMessage);
                            fileStreamWriter.WriteLine("---------------------------------------------------------------------------");
                        }
                    }
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// This class stores the database of names and locations and provides methods to 
    /// access them in a thread safe manner.
    /// </summary>
    static class Database
    {
        #region Members

        /// <summary>
        /// Dictionary to store user data, key is the name; value is the location.
        /// </summary>
        private static Dictionary<string, string> userDictionary = new Dictionary<string, string>();
        // Streamwriter to write to the file.
        private static StreamWriter fileStreamWriter;
        // Streamreader for loading in from the file.
        private static StreamReader fileStreamReader;
        // Lock object so no two threads can attempt to change values in the database at the same time.
        private static readonly object DatabaseLock = new object();
        private static string DatabaseFilePath = null;
        public static bool savingToFileEnabled = false;

        #endregion

        #region Methods

        /// <summary>
        /// Sets the file path of the database and enables saving to file. Checks if user has permission to 
        /// create files in the directory. If user doesnt have permission returns as false. If finds a previous
        /// file in directory it is loaded.
        /// </summary>
        /// <param name="pFilePath">File path where the database will be stored</param>
        /// <returns></returns>
        public static bool setFilePath(string pFilePath)
        {
            try
            {
                using (File.Open(pFilePath, FileMode.OpenOrCreate)) { }
            }
            catch (Exception)
            {
                return false;
            }

            // If previous database file exists it is loaded into the database member dictionary.
            if (File.Exists(pFilePath))
            {
                loadDatabase(pFilePath);
                logger.Log("Database has been loaded from previous file", logger.MessageType.general);
            }

            DatabaseFilePath = pFilePath;
            savingToFileEnabled = true;
            return true;
        }

        /// <summary>
        /// Returns the file path of the database.
        /// </summary>
        public static string getFilePath()
        {
            return DatabaseFilePath;
        }

        /// <summary>
        /// Adds an entry to the database. Returns true if successfully added,
        /// false if the person was already in the database.
        /// </summary>
        /// <param name="name">Name of the person to add to the database</param>
        /// <param name="location">Location to give to the person being added.</param>
        public static bool add(string name, string location)
        {
            lock (DatabaseLock)
            {
                // Checks if the database already contains a person with the same name.
                if (!userDictionary.ContainsKey(name))
                {
                    userDictionary.Add(
                        key: name,
                        value: location);
                    return true;
                }
                else
                {
                    logger.Log(string.Format("User ({0}) was already in the database", name), logger.MessageType.error);
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if the person is in the database.
        /// </summary>
        /// <param name="name">Name of the person to search for in the database.</param>
        /// <returns></returns>
        public static bool isPersonInDatabase(string name)
        {
            lock (DatabaseLock)
            {

                if (userDictionary.ContainsKey(name))
                    return true;
                else
                    return false;
            }
        }

        /// <summary>
        /// Returns the location of the person in the database
        /// </summary>
        /// <param name="name"></param>
        /// <param name="location"></param>
        /// <returns></returns>
        public static string getLocation(string name)
        {
            lock (DatabaseLock)
            {
                return userDictionary[name];
            }
        }

        /// <summary>
        /// Sets the location of the user.
        /// </summary>
        /// <param name="name">Name of the person the location is updated for.</param>
        /// <returns></returns>
        /// 
        public static void setLocation(string name, string location)
        {
            lock (DatabaseLock)
            {
                userDictionary[name] = location;
            }
        }

        /// <summary>
        ///  Saves the server to the directory specified.
        /// </summary>
        /// <param name="pFilePath">Directory og where the server will save to.</param>
        public static void saveDatabase()
        {
            using (fileStreamWriter = new StreamWriter(DatabaseFilePath, false))
            {
                // DatabaseFilePath = DatabaseFilePath.Replace(' ', null);
                // Writes every entry in the database to the file with a name on one line and the 
                // corresponding location on the next.
                foreach (KeyValuePair<string, string> entry in userDictionary)
                {
                    fileStreamWriter.WriteLine(entry.Key);
                    fileStreamWriter.WriteLine(entry.Value);
                }
            }
        }

        /// <summary>
        /// Loads the server from the directory specified.Returns true if successfully loaded 
        /// and false if failed to load.
        /// </summary>
        public static bool loadDatabase(string pFilePath)
        {
            try
            {
                using (fileStreamReader = new StreamReader(pFilePath))
                    do
                    {
                        add(
                            name: fileStreamReader.ReadLine(),
                            location: fileStreamReader.ReadLine());
                    } while (fileStreamReader.Peek() != -1);
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }
        #endregion
    }
}
