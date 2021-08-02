using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GovanifY.Utility;
using Microsoft.VisualBasic;

namespace SignalEditor
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            groupBox1.Size = new Size(136, 0);
        }

        public List<Signal> signals;

        #region Classes
        public class Signal
        {
            public ushort Frame { get; set; }
            public ushort Type { get; set; }
            public uint Data { get; set; }
            public string Str { get; set; }
        }

        public class NodeSorter : System.Collections.IComparer
        {
            public NodeSorter() { }

            public int Compare(object x, object y)
            {
                TreeNode tx = x as TreeNode;
                TreeNode ty = y as TreeNode;

                string s1 = tx.Text;
                while (s1.Length > 0 && Char.IsDigit(s1.Last())) s1 = s1.TrimEnd(s1.Last());
                s1 = s1 + tx.Text.Substring(s1.Length).PadLeft(12, '0');

                string s2 = tx.Text;
                while (s2.Length > 0 && Char.IsDigit(s2.Last())) s2 = s2.TrimEnd(s2.Last());
                s2 = s2 + ty.Text.Substring(s2.Length).PadLeft(12, '0');

                return string.Compare(s1, s2);
            }
        }
        #endregion

        #region Code Helpers
        private byte[] IntToByteArray(uint data)
        {
            byte[] bytes = new byte[4];
            bytes[0] = (byte)data;
            bytes[1] = (byte)(((uint)data >> 8) & 0xFF);
            bytes[2] = (byte)(((uint)data >> 16) & 0xFF);
            bytes[3] = (byte)(((uint)data >> 24) & 0xFF);
            return bytes;
        }
        private byte IntToByte(uint data, int index)
        {
            if (index > 4)
                index = 4;
            if (index < 0)
                index = 0;

            byte[] bytes = new byte[4];
            bytes[0] = (byte)data;
            bytes[1] = (byte)(((uint)data >> 8) & 0xFF);
            bytes[2] = (byte)(((uint)data >> 16) & 0xFF);
            bytes[3] = (byte)(((uint)data >> 24) & 0xFF);
            return bytes[index];
        }

        private static byte[] ConvertHexStringToByteArray(string hexString)
        {
            if (hexString.Length % 2 != 0)
            {
                return null;
            }

            byte[] data = new byte[hexString.Length / 2];
            for (int index = 0; index < data.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                try
                {
                    data[index] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
                catch (FormatException e)
                {
                    return null;
                }
            }

            return data;
        }

        private static uint ReadUInt32FromArray(byte[] array)
        {
            return (uint)(array[0] | (array[1] << 8) | (array[2] << 16) | (array[3] << 24));
        }
        private static uint ReadUInt32FromArray(byte[] array, uint index)
        {
            return (uint)(array[index + 0] | (array[index + 1] << 8) | (array[index + 2] << 16) | (array[index + 3] << 24));
        }

        private static int ReadInt32FromArray(byte[] array)
        {
            return (int)(array[0] | (array[1] << 8) | (array[2] << 16) | (array[3] << 24));
        }
        private static int ReadInt32FromArray(byte[] array, uint index)
        {
            return (int)(array[index + 0] | (array[index + 1] << 8) | (array[index + 2] << 16) | (array[index + 3] << 24));
        }

        #endregion

        #region Tool Strip
        private void TS_Open_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Signal or BCH (*.bin/*.bch)|*.bin;*.bch|All files (*.*)|*.*";
                //openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;
                openFileDialog.Title = "Open Signal File";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    OpenFile(openFileDialog.FileName);
                }
            }
        }

        private void TS_New_Click(object sender, EventArgs e)
        {
            TV_siglist.Nodes.Clear();
            TV_siglist.Nodes.Add("New Signal File");
        }

        private void TS_Save_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "signal.bin (*.bin)|*.bin|All files (*.*)|*.*";
                saveFileDialog.FilterIndex = 1;
                saveFileDialog.RestoreDirectory = true;
                saveFileDialog.Title = "Save Signal File";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllBytes(saveFileDialog.FileName, TreeViewToSignals());
                }
            }
        }

        #endregion

        #region Main Code
        private void OpenFile(string infile)
        {
            FileStream fs = new FileStream(infile, FileMode.Open);
            BinaryStream bin = new BinaryStream(fs, true, false);
            var ShiftJIS = Encoding.GetEncoding(932);

            ushort magic = bin.ReadUInt16();
            if (magic == 21313 || magic == 17218)
            {
                //when reading a bch file skip to the section with signals based on 0x4153 magic
                long bchoffset = 0;
                if (magic == 17218)
                {
                    while (magic != 21313)
                    {
                        magic = bin.ReadUInt16();
                    }
                    bchoffset = bin.Tell() - 0x2;
                }

                TV_siglist.Nodes.Clear();
                signals = new List<Signal>();
                ushort count = bin.ReadUInt16();
                //skip to strings
                bin.Seek(count * 0x8, SeekOrigin.Current);
                byte[] strsection;
                if (bin.Tell() != fs.Length)
                {
                    strsection = bin.ReadBytes(Convert.ToInt32(fs.Length - bin.Tell()));
                }
                else
                    strsection = new byte[] { 0x0 };

                //return to signal section
                bin.Seek(0x4 + bchoffset, SeekOrigin.Begin);
                for (int i = 0; i < count; i++)
                {
                    ushort frame = bin.ReadUInt16();
                    ushort type = bin.ReadUInt16();
                    uint data = bin.ReadUInt32();
                    string str = "";
                    if (type == 0x43 || type == 0x45 || type == 0x48 || type == 0x49 || type == 0x2e || type == 0x33)
                    {
                        str = ShiftJIS.GetString(strsection.Skip(Convert.ToInt32(data - ((count * 8) + 0x4))).TakeWhile(b => b != 0).ToArray());
                    }
                    signals.Add(new Signal { Frame = frame, Type = type, Data = data, Str = str });
                }
                bin.Close();
                fs.Close();

                SignalToTreeNodes(infile);
            }
            else
            {
                MessageBox.Show("File is not signal file or BCH", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                bin.Close();
                return;
            }
            
        }

        private void SignalToTreeNodes(string infile)
        {
            if (signals.Count <= 0)
                return;

            TV_siglist.Nodes.Add(Path.GetFileNameWithoutExtension(infile));
            foreach (var signal in signals)
            {
                if (!TV_siglist.Nodes[0].Nodes.ContainsKey(Convert.ToString(signal.Frame)))
                {
                    TV_siglist.Nodes[0].Nodes.Add(Convert.ToString(signal.Frame), $"Frame: {Convert.ToString(signal.Frame)}");
                }
                var tvframenode = TV_siglist.Nodes[0].Nodes.IndexOfKey(Convert.ToString(signal.Frame));
                TV_siglist.Nodes[0].Nodes[tvframenode].Nodes.Add(Convert.ToString(signal.Type), $"Type: {Convert.ToString(signal.Type)}");

                var tvtypenode = TV_siglist.Nodes[0].Nodes[tvframenode].Nodes.IndexOfKey(Convert.ToString(signal.Type));
                if (signal.Type == 0x43 || signal.Type == 0x45 || signal.Type == 0x48 || signal.Type == 0x49 || signal.Type == 0x2e || signal.Type == 0x33)
                {
                    TV_siglist.Nodes[0].Nodes[tvframenode].Nodes[tvtypenode].Nodes.Add(Convert.ToString(signal.Data), signal.Str);
                }
                //else if (signal.Type == 0x4)
                //{
                //    TV_siglist.Nodes[0].Nodes[tvframenode].Nodes[tvtypenode].Nodes.Add(Convert.ToString(signal.Data), Convert.ToString(Convert.ToSingle(signal.Data)));
                //}
                else
                {
                    TV_siglist.Nodes[0].Nodes[tvframenode].Nodes[tvtypenode].Nodes.Add(Convert.ToString(signal.Data), $"0x{BitConverter.ToString(IntToByteArray(signal.Data)).Replace("-","")}");
                }
            }
            TV_siglist.TreeViewNodeSorter = new NodeSorter();
            TV_siglist.Sort();
        }

        private void TV_siglist_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (TV_siglist.SelectedNode.Text.StartsWith("Type:"))
            {
                textBox1.Text = $"{TV_siglist.SelectedNode.Text.Replace("Type: ", "")}";
            }
            else if (TV_siglist.SelectedNode.Text.StartsWith("0x"))
            {
                textBox1.Text = $"{TV_siglist.SelectedNode.Text.Replace("0x", "")}";
            }
            else if (TV_siglist.SelectedNode.Text.StartsWith("Frame:"))
            {
                textBox1.Text = $"{TV_siglist.SelectedNode.Text.Replace("Frame: ", "")}";
            }
            else if (TV_siglist.SelectedNode.Level == 0)
                textBox1.Clear();
            else
                textBox1.Text = TV_siglist.SelectedNode.Text;
        }

        private byte[] TreeViewToSignals()
        {
            if (TV_siglist.Nodes.Count <= 0 || TV_siglist.Nodes[0].Nodes.Count <= 0)
                return null;
            int signalcount = 0;
            List<uint> pointerPos = new List<uint>();
            List<string> Strings = new List<string>();
            MemoryStream ms = new MemoryStream();
            var ShiftJIS = Encoding.GetEncoding(932);
            using (BinaryStream bin = new BinaryStream(ms, true))
            {
                bin.Write((short)21313);
                bin.Write((short)0);
                foreach (TreeNode Frame in TV_siglist.Nodes[0].Nodes)
                {
                    foreach (TreeNode Type in Frame.Nodes)
                    {
                        signalcount++;
                        bin.Write(Convert.ToUInt16(Frame.Text.Replace("Frame: ", "")));
                        bin.Write(Convert.ToUInt16(Type.Text.Replace("Type: ", "")));
                        if (Type.Text == "Type: 67" || Type.Text == "Type: 69" || Type.Text == "Type: 72" || Type.Text == "Type: 73" || Type.Text == "Type: 46" || Type.Text == "Type: 51")
                        {
                            pointerPos.Add((uint)bin.Tell());
                            bin.Write((uint)0);
                            Strings.Add(Type.Nodes[0].Text);
                        }
                        else
                        {
                            bin.Write(ConvertHexStringToByteArray(Type.Nodes[0].Text.Replace("0x", "")));
                        }
                    }
                }

                for (int i = 0; i < Strings.Count; i++)
                {
                    long currentPos = bin.Tell();
                    byte[] linebytes = ShiftJIS.GetBytes(Strings[i]);
                    bin.Write(linebytes);
                    bin.Write((byte)0);

                    bin.Seek(pointerPos[i], SeekOrigin.Begin);
                    bin.Write((uint)currentPos);
                    bin.Seek(0, SeekOrigin.End);
                }

                while ((int)bin.Tell() % 4 != 0)
                {
                    bin.Write((byte)0);
                }

                bin.Seek(2, SeekOrigin.Begin);
                bin.Write((ushort)signalcount);
            }
            return ms.ToArray();
        }

        #endregion

        #region Buttons
        private void B_Remove_Click(object sender, EventArgs e)
        {
            if (TV_siglist.SelectedNode.Level != 0 || TV_siglist.SelectedNode.Level != 3)
            {
                if (TV_siglist.SelectedNode.Text.StartsWith("Frame:") && MessageBox.Show("Removing the frame will remove all signals under this frame\nAre your sure you want to continute?", "Prompt", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
                {
                    TV_siglist.SelectedNode.Remove();
                }
                else
                    TV_siglist.SelectedNode.Remove();
            }
            TV_siglist.TreeViewNodeSorter = new NodeSorter();
            TV_siglist.Sort();
        }

        private void B_Update_Click(object sender, EventArgs e)
        {
            if (textBox1.Text.Length <= 0 || TV_siglist.SelectedNode == null)
                return;
            if (TV_siglist.SelectedNode.Text.StartsWith("Type:"))
            {
                try
                {
                    Convert.ToInt16(textBox1.Text);
                }
                catch (FormatException error)
                {
                    MessageBox.Show("Input is not a vaid signal type", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                TV_siglist.SelectedNode.Text = $"Type: {textBox1.Text}";
            }
            else if (TV_siglist.SelectedNode.Text.StartsWith("0x"))
            {
                byte[] bytetest = ConvertHexStringToByteArray(textBox1.Text);
                if (bytetest == null)
                {
                    MessageBox.Show("Input is not a vaid hex string", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                TV_siglist.SelectedNode.Text = $"0x{textBox1.Text.ToUpper()}"; ;
            }
            else if (TV_siglist.SelectedNode.Text.StartsWith("Frame:"))
            {
                foreach (TreeNode node in TV_siglist.Nodes[0].Nodes)
                {
                    if (node.Text == $"Frame: {textBox1.Text}")
                    {
                        MessageBox.Show("Frame already exists", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                try
                {
                    Convert.ToInt16(textBox1.Text);
                }
                catch (FormatException error)
                {
                    MessageBox.Show("Input is not a number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                TV_siglist.SelectedNode.Text = $"Frame: {textBox1.Text}";
            }
            else if (TV_siglist.SelectedNode.Level == 0)
                return;
            else
            {
                TV_siglist.SelectedNode.Text = textBox1.Text;
            }
            
            TV_siglist.TreeViewNodeSorter = new NodeSorter();
            TV_siglist.Sort();
            textBox1.Clear();
        }

        private void B_Add_Click(object sender, EventArgs e)
        {
            if (TV_siglist.SelectedNode == null || !TV_siglist.SelectedNode.Text.StartsWith("Frame:"))
            {
                MessageBox.Show("Select a Frame to add signal", "Prompt", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }
            string input = Interaction.InputBox("Signal type to add", "Signal Input", "0").Replace(" ", "");
            try
            {
                Convert.ToInt16(input);
            }
            catch (FormatException error)
            {
                MessageBox.Show("Input is not a number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (TV_siglist.SelectedNode.Nodes.ContainsKey(input))
            {
                MessageBox.Show("Signal type already exists", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            else
            {
                int i = Convert.ToInt16(input);
                if (i == 67 || i == 69 || i == 72)
                {
                    TV_siglist.SelectedNode.Nodes.Add(input, $"Type: {input}").Nodes.Add("0", "Null");
                }
                else if (i == 73)
                {
                    TV_siglist.SelectedNode.Nodes.Add(input, $"Type: {input}").Nodes.Add("0", "NML1");
                }
                else
                {
                    TV_siglist.SelectedNode.Nodes.Add(input, $"Type: {input}").Nodes.Add("0", "0x00000000");
                }
                TV_siglist.TreeViewNodeSorter = new NodeSorter();
                TV_siglist.Sort();
            }
        }

        private void B_Add_Frame_Click(object sender, EventArgs e)
        {
            if (TV_siglist.Nodes.Count <= 0)
            {
                MessageBox.Show("Open or create a new Signal file first", "Prompt", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }
            string input = Interaction.InputBox("Frame to insert", "Frame Input", "0").Replace(" ","");
            try
            {
                Convert.ToInt16(input);
            }
            catch (FormatException error)
            {
                MessageBox.Show("Input is not a number", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            foreach (TreeNode node in TV_siglist.Nodes[0].Nodes)
            {
                if (node.Text == $"Frame: {input}")
                {
                    MessageBox.Show("Frame already exists", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            TV_siglist.Nodes[0].Nodes.Add(input, $"Frame: {input}");
            TV_siglist.TreeViewNodeSorter = new NodeSorter();
            TV_siglist.Sort();
        }

        private void B_Convert_Click(object sender, EventArgs e)
        {
            richTextBox1.Clear();
            byte[] signaldata = TreeViewToSignals();
            if (signaldata == null)
                return;

            for (uint i = 0; i < signaldata.Length / 4; i++)
            {
                int data = ReadInt32FromArray(signaldata, i * 4);
                richTextBox1.AppendText(data + Environment.NewLine);
            }
        }

        private void B_Clear_Click(object sender, EventArgs e)
        {
            richTextBox1.Clear();
        }

        #endregion

        #region Number Converter
        private bool lockTB2 = false;
        private bool lockTB3 = false;

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            if (textBox2.Text.Length <= 0)
                return;
            
            if (lockTB2 == false)
            {
                lockTB3 = true;
                try
                {
                    Convert.ToInt32(textBox2.Text);
                }
                catch (FormatException)
                {
                    return;
                }

                textBox3.Clear();
                textBox3.Text = Convert.ToString(Convert.ToInt32(textBox2.Text), 16).ToUpper();
            }
            lockTB3 = false;
        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            if (textBox3.Text.Length <= 0)
                return;

            if (lockTB3 == false)
            {
                lockTB2 = true;
                try
                {
                    Int64.Parse(textBox3.Text, NumberStyles.AllowHexSpecifier);
                }
                catch (FormatException)
                {
                    return;
                }

                textBox2.Clear();
                textBox2.Text = Convert.ToString(Convert.ToInt32(Int64.Parse(textBox3.Text, NumberStyles.AllowHexSpecifier)));
            }
            lockTB2 = false;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            textBox2.Clear();
            textBox3.Clear();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (groupBox1.Size.Height == 0)
            {
                groupBox1.Size = new Size(136, 126);
                button2.Text = "v";
            }
            else
            {
                groupBox1.Size = new Size(136, 0);
                button2.Text = "^";
            }
        }
        #endregion
    }
}
