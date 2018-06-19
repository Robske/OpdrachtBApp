// Standard namespaces (dont remove)
using System;
using Android.App;
using Android.Widget;
using Android.OS;
using Android.Support.Design.Widget;
using Android.Support.V7.App;
using Android.Views;

// Aditional added namespaces
//using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Timers;
using System.Collections.Generic;

using Android.Graphics;


namespace My_Room
{
	[Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
	public class MainActivity : AppCompatActivity
	{
        // Variables (components/controls)
        //Controls the GUI
        Button buttonConnect;
        EditText editTextIP;
        TextView textViewConnectStatus, textViewDoor, textViewDoorStatus;
        TextView textView = null;

        Socket socket = null;
        Timer timerSocket;
        List<Tuple<string, TextView>> commandList = new List<Tuple<string, TextView>>();  // List for commands and response places on UI
        int listIndex = 0;
        string result;

        protected override void OnCreate(Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			SetContentView(Resource.Layout.activity_main);

			Android.Support.V7.Widget.Toolbar toolbar = FindViewById<Android.Support.V7.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

			FloatingActionButton fab = FindViewById<FloatingActionButton>(Resource.Id.fab);
            fab.Click += FabOnClick;

            // find and set controls
            buttonConnect = FindViewById<Button>(Resource.Id.buttonConnect);
            editTextIP = FindViewById<EditText>(Resource.Id.editTextIP);
            textViewConnectStatus = FindViewById<TextView>(Resource.Id.textViewConnectedStatus);
            textViewDoor = FindViewById<TextView>(Resource.Id.textViewDoorOpen);                    // Not used
            textViewDoorStatus = FindViewById<TextView>(Resource.Id.textViewDoorStatus);

            // Init commands, scheduled by timerSocket
            commandList.Add(new Tuple<string, TextView>("d", textViewDoorStatus));

            timerSocket = new System.Timers.Timer() { Interval = 250, Enabled = false }; // Interval >= 750
            timerSocket.Elapsed += (obj, args) =>
            {
                // Thread sends commands to arduino
                RunOnUiThread(() =>
                {
                    if (socket != null) // only if socket exists
                    {
                        // Send a command to the Arduino server on every tick (loop though list)
                        result = executeCommand(commandList[listIndex].Item1);
                        textView = commandList[listIndex].Item2;
                        Color color = Color.White;

                        // Check which value was send, and anticipate on it
                        switch (commandList[listIndex].Item1)
                        {
                            case "d":
                                if (result == "Clos") { color = Color.Red; result = "Closed"; }
                                else if (result == "Open") { color = Color.Green; }

                                textView.SetTextColor(color);
                                UpdateGUI(result, commandList[listIndex].Item2);
                                break;
                            default:
                                break;
                        }

                        if (++listIndex >= commandList.Count) listIndex = 0;
                    }
                });
            };

            //Add the "Connect" button handler.
            if (buttonConnect != null)  // if button exists
            {
                buttonConnect.Click += (sender, e) =>
                {
                    //Validate the user input (IP address and port)
                    if (CheckValidIpAddress(editTextIP.Text))
                    {
                        ConnectSocket(editTextIP.Text, 3300);
                    }
                    else UpdateConnectionState(3, "Please check IP");
                };
            }

        }

        /// <summary>
        /// Check if IP address is valid
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        private bool CheckValidIpAddress(string ip)
        {
            if (ip != "")
            {
                //Check user input against regex (check if IP address is not empty).
                Regex regex = new Regex("\\b((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)(\\.|$)){4}\\b");
                Match match = regex.Match(ip);
                return match.Success;
            }
            else return false;
        }

        /// <summary>
        /// Connects to a socked
        /// </summary>
        /// <param name="ip">IP address</param>
        /// <param name="prt">Port to connect to</param>
        public void ConnectSocket(string ip, int prt)
        {
            RunOnUiThread(() =>
            {
                if (socket == null) // create new socket
                {
                    UpdateConnectionState(1, "...");
                    try  // to connect to the server (Arduino).
                    {
                        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        socket.Connect(new IPEndPoint(IPAddress.Parse(ip), prt));
                        if (socket.Connected)
                        {
                            UpdateConnectionState(2, "STATUS");
                            timerSocket.Enabled = true;                //Activate timer for communication with Arduino     
                        }
                    }
                    catch (Exception exception)
                    {
                        timerSocket.Enabled = false;
                        if (socket != null)
                        {
                            socket.Close();
                            socket = null;
                        }
                        UpdateConnectionState(4, exception.Message);
                    }
                }
                else // disconnect socket
                {
                    socket.Close(); socket = null;
                    timerSocket.Enabled = false;
                    UpdateConnectionState(4, "STATUS");
                }
            });
        }

        /// <summary>
        /// Updates connecting status
        /// </summary>
        /// <param name="state"></param>
        /// <param name="text"></param>
        public void UpdateConnectionState(int state, string text)
        {
            // connectButton
            string butConText = "Connect";  // default text
            bool butConEnabled = true;      // default state
            Color color = Color.Red;        // default color

            //Set "Connect" button label according to connection state.
            if (state == 1)
            {
                butConText = "Please wait";
                color = Color.Orange;
                butConEnabled = false;
            }
            else if (state == 2)
            {
                butConText = "Disconnect";
                color = Color.Green;
                //FindViewById<Button>(Resource.Id.textView1).Text = color.ToString();
            }

            //Edit the control's properties on the UI thread
            RunOnUiThread(() =>
            {
                textViewConnectStatus.SetTextColor(color);
                textViewConnectStatus.Text = text;

                buttonConnect.Text = butConText;
                buttonConnect.Enabled = butConEnabled;
            });
        }

        /// <summary>
        /// Updates GUI based on Arduino response
        /// </summary>
        /// <param name="result"></param>
        /// <param name="textview"></param>
        public void UpdateGUI(string result, TextView textview)
        {
            RunOnUiThread(() =>
            {
                textview.Text = result;
            });
        }

        /// <summary>
        /// Sends value through socket
        /// </summary>
        /// <param name="cmd"></param>
        /// <returns></returns>
        public string executeCommand(string cmd)
        {

            byte[] buffer = new byte[4]; // response is always 4 bytes
            int bytesRead = 0;
            string result = "---";

            if (socket != null)
            {
                //Send command to server
                socket.Send(Encoding.ASCII.GetBytes(cmd));

                try //Get response from server
                {
                    //Store received bytes (always 4 bytes, ends with \n)
                    bytesRead = socket.Receive(buffer);  // If no data is available for reading, the Receive method will block until data is available,
                    //Read available bytes.              // socket.Available gets the amount of data that has been received from the network and is available to be read
                    while (socket.Available > 0) bytesRead = socket.Receive(buffer);
                    if (bytesRead == 4)
                        result = Encoding.ASCII.GetString(buffer, 0, bytesRead); // skip \n
                    else result = "err";
                }
                catch (Exception exception)
                {
                    result = exception.ToString();
                    if (socket != null)
                    {
                        socket.Close();
                        socket = null;
                    }
                    UpdateConnectionState(3, result);
                }
            }

            return result;
        }

        // Standard functions from here
        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        private void FabOnClick(object sender, EventArgs eventArgs)
        {
            View view = (View) sender;
            Snackbar.Make(view, "Replace with your own action", Snackbar.LengthLong)
                .SetAction("Action", (Android.Views.View.IOnClickListener)null).Show();
        }
	}
}

