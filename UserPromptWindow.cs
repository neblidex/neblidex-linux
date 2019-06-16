using System;
using Gtk;
using System.Threading;

namespace NebliDex_Linux
{
    public partial class UserPromptWindow : Gtk.Window
    {
		public string final_response = "";
        private string backup = "";
        private int tries = -1;
        bool is_password = true;
		public ManualResetEvent waiting = null;

		public UserPromptWindow(string ques, bool password) :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
            //Old window height is 155
			Prompt.Markup = "<b>"+ques+"</b>";
            is_password = password;
            if (is_password == false)
            {
                tries = 0;

				User_Response_Pass.Visible = false;
				User_Response.Visible = true;
            }
			OK_Button.Clicked += Save_Info;
			this.DeleteEvent += UserPrompt_Closed;
        }

        private void Save_Info(object sender, EventArgs e)
        {
            string response = "";
            if (is_password == true)
            {
				response = User_Response_Pass.Text;
            }
            else
            {
                response = User_Response.Text;
            }

            response = response.Trim();

            if (response.Length < 6 && response.Length > 0)
            {
                App.MessageBox(this,"Notice","This password is too short. Please make it at least 6 characters.","OK");
            }
            else
            {
                if (tries == 0 && response.Length > 0)
                {
                    //We want the user to re-enter the password
					App.MessageBox(this,"Notice","For confirmation, please re-enter previously entered password. Do not lose this password. There is no option to recover it!","OK");
                    Prompt.Markup = "<b>Please re-enter the password\nfor confirmation.</b>";
                    backup = response;
                    User_Response.Text = "";
                    tries++;
                    return;
                }
                else if (tries == 1)
                {
                    if (response.Equals(backup) == false)
                    {
						App.MessageBox(this, "Notice","The password doesn't match the previously entered.","OK");
                        return; //For the user to try again
                    }
                }
                final_response = response;

				UserPrompt_Closed(null, null); //Clear the wait
				this.Destroy();
            }
        }

		private void UserPrompt_Closed(object sender, DeleteEventArgs args)
        {
            if (waiting != null)
            {
                //Allow the calling thread to continue
                waiting.Set();
            }
        }
    }
}
