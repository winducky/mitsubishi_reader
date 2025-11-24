using HslCommunication;
using HslCommunication.Profinet.Melsec;
using Npgsql;
using ScottPlot;
using ScottPlot.ArrowShapes;
using ScottPlot.WinForms;
using System;
using System.Windows.Forms;

namespace PLC
{
    public partial class D : Form
    {
        private string configPath = "";
        private string ip_server = "";
        private string database_name = "";
        private string user = "";
        private string pass = "";
        private int port;
        private string ip_plc = "";
        private int port_plc;
        private string[] addresses_plc_get = { "D100", "W30", "W2D", "D330", "D332", "D334", "D336", "W124", "W40" };
        private Dictionary<string, string>? dbConfig = null;
        private System.Windows.Forms.Timer? plcTimer_Read = null;
        private System.Windows.Forms.Timer? plcTimer_Write = null;
        private MelsecMcNet? plc = null;
        private FormsPlot chart_extruder = null!;
        private List<double> weight_upper_limit_value = new List<double>() {};
        private List<double> weight_lower_limit_value = new List<double>() {};
        private List<double> weight_setting_value = new List<double>() {};
        private List<double> cv_speed_value = new List<double>() {};
        private List<double> actual_weight_value = new List<double>() {};
        private List<DateTime> time = new List<DateTime>() {};
        private ScottPlot.Plottables.Scatter weight_upper_limit_line = null!;
        private ScottPlot.Plottables.Scatter weight_lower_limit_line = null!;
        private ScottPlot.Plottables.Scatter weight_setting_line = null!;
        private ScottPlot.Plottables.Scatter cv_speed_line = null!;
        private ScottPlot.Plottables.Scatter actual_weight_line = null!;
        private Random random = new Random();
        public D()
        {
            InitializeComponent();
            this.FormClosing += Form1_FormClosing;
            chart_extruder = new FormsPlot();
            chart_extruder.Dock = DockStyle.Fill;
            panel1.Controls.Add(chart_extruder);
            // tạo signal liên kết trực tiếp với list
            weight_upper_limit_line = chart_extruder.Plot.Add.Scatter(time, weight_upper_limit_value);
            weight_lower_limit_line = chart_extruder.Plot.Add.Scatter(time, weight_lower_limit_value);
            weight_setting_line = chart_extruder.Plot.Add.Scatter(time, weight_setting_value);
            cv_speed_line = chart_extruder.Plot.Add.Scatter(time, cv_speed_value);
            actual_weight_line = chart_extruder.Plot.Add.Scatter(time, actual_weight_value);

            // phân trục
            weight_upper_limit_line.Axes.YAxis = chart_extruder.Plot.Axes.Left;
            weight_lower_limit_line.Axes.YAxis = chart_extruder.Plot.Axes.Left;
            weight_setting_line.Axes.YAxis = chart_extruder.Plot.Axes.Left;
            cv_speed_line.Axes.YAxis = chart_extruder.Plot.Axes.Right;
            actual_weight_line.Axes.YAxis = chart_extruder.Plot.Axes.Left;

            // nhãn trục
            chart_extruder.Plot.Axes.Left.Label.Text = "(g)";
            chart_extruder.Plot.Axes.Right.Label.Text = "(m/min)";
            chart_extruder.Plot.Axes.Bottom.Label.Text = "(Time)";

            // màu nhãn trục
            chart_extruder.Plot.Axes.Left.Label.ForeColor = weight_upper_limit_line.Color; 
            chart_extruder.Plot.Axes.Right.Label.ForeColor = cv_speed_line.Color;

            // đặt legend
            weight_upper_limit_line.LegendText = "Weight Upper Limit";
            weight_lower_limit_line.LegendText = "Weight Lower Limit";
            weight_setting_line.LegendText = "Weight Setting";
            cv_speed_line.LegendText = "CV Speed";
            actual_weight_line.LegendText = "Actual Weight";
            
            // Đặt định dạng trục X
            chart_extruder.Plot.Axes.DateTimeTicksBottom();
            chart_extruder.Plot.Axes.AutoScale();
            chart_extruder.Refresh();
        }
        private void D_Load(object sender, EventArgs e)
        {
            // Lấy đường dẫn file exe và config
            string exeDir = AppContext.BaseDirectory;
            configPath = Path.Combine(exeDir, "app.config");

            // Kiểm tra nếu chưa có file → tạo mặc định
            if (!File.Exists(configPath))
            {
                string defaultConfig =
                @"ip_server=192.168.130.234
database_name=irv_bc
user=postgres
pass=postgres
port=5432
ip_plc=192.168.3.39
port_plc=5555";
                File.WriteAllText(configPath, defaultConfig);
                MessageBox.Show("File app.config chưa tồn tại, đã tạo mặc định.");
            }

            // Đọc file config
            dbConfig = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(configPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                    dbConfig[parts[0].Trim()] = parts[1].Trim();
            }
            // Gán vào biến class sau khi đọc xong toàn bộ file
            ip_server = dbConfig.ContainsKey("ip_server") ? dbConfig["ip_server"] : "127.0.0.1";
            database_name = dbConfig.ContainsKey("database_name") ? dbConfig["database_name"] : "postgres";
            user = dbConfig.ContainsKey("user") ? dbConfig["user"] : "postgres";
            pass = dbConfig.ContainsKey("pass") ? dbConfig["pass"] : "";
            port = dbConfig.ContainsKey("port") && int.TryParse(dbConfig["port"], out int p) ? p : 5432;
            ip_plc = dbConfig.ContainsKey("ip_plc") ? dbConfig["ip_plc"] : "192.168.3.39";
            port_plc = dbConfig.ContainsKey("port_plc") && int.TryParse(dbConfig["port_plc"], out int pp) ? pp : 5555;

        }

        // ==================== Sự kiện đóng Form ====================
        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                if (plcTimer_Read != null)
                {
                    plcTimer_Read.Stop();
                    plcTimer_Read.Tick -= PlcTimer_Read_Tick;
                    plcTimer_Read = null;
                }

                if (plcTimer_Write != null)
                {
                    plcTimer_Write.Stop();
                    plcTimer_Write.Tick -= PlcTimer_Write_Tick;
                    plcTimer_Write = null;
                }


                if (plc != null)
                {
                    plc.ConnectClose();
                    plc = null;
                }
            }
            catch { }
        }

        // ==================== Hàm cập nhật biểu đồ ====================
        private void update_chart(double a, double b, double c, double d, double e, DateTime f)
        {
            // thêm dữ liệu mới
            //weight_upper_limit_value.Add(random.Next(1,9));
            //weight_lower_limit_value.Add(random.Next(1, 9));
            //weight_setting_value.Add(random.Next(1, 9));
            //cv_speed_value.Add(random.Next(1, 9));
            //actual_weight_value.Add(random.Next(1, 9));
            //time.Add(DateTime.Now);

            weight_upper_limit_value.Add(a);
            weight_lower_limit_value.Add(b);
            weight_setting_value.Add(c);
            cv_speed_value.Add(d);
            actual_weight_value.Add(e);
            time.Add(f);

            // autoscale (nếu muốn)
            chart_extruder.Plot.Axes.AutoScale();

            // refresh để cập nhật
            chart_extruder.Refresh();
        }

        // ==================== Event click nút tìm kiếm ====================
        private void size_search_Click(object? sender, EventArgs e)
        {
            try
            {
                // Lấy giá trị từ control nhập
                string managementNo = management_search.Text; // nếu NumericUpDown thì dùng .Value.ToString()
                if (string.IsNullOrWhiteSpace(managementNo))
                {
                    MessageBox.Show("Vui lòng nhập Management No");
                    return;
                }

                // Chuỗi kết nối PostgreSQL dùng các biến config đã đọc
                string connString = $"Host={ip_server};Port={port};Username={user};Password={pass};Database={database_name}";

                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();

                    // Sử dụng parameter để tránh SQL Injection
                    string sql = "SELECT * FROM bc_size WHERE management_no = @mgmt_no LIMIT 1";
                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@mgmt_no", managementNo);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                // Gán dữ liệu vào các label
                                management_no_view.Text = reader["management_no"].ToString();
                                type_view.Text = reader["type"].ToString();
                                size_view.Text = reader["size"].ToString();
                                pattern_view.Text = reader["pattern"].ToString();
                                mascot_name_view.Text = reader["mascot_name"].ToString();
                                kind_view.Text = reader["kind"].ToString();
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy dữ liệu!");
                                // Xóa dữ liệu cũ
                                management_no_view.Text = "";
                                type_view.Text = "";
                                size_view.Text = "";
                                pattern_view.Text = "";
                                mascot_name_view.Text = "";
                                kind_view.Text = "";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi truy vấn Database: " + ex.Message);
            }
        }

        // ==================== Event click nút ====================
        private void plc_read_Click(object? sender, EventArgs e)
        {
            // Đóng timer cũ nếu có
            if (plcTimer_Read != null)
            {
                plcTimer_Read.Stop();
                plcTimer_Read.Tick -= PlcTimer_Read_Tick;
                plcTimer_Read = null;
            }

            // Đóng PLC cũ nếu có
            if (plc != null)
            {
                plc.ConnectClose();
                plc = null;
            }

            // Tạo PLC mới
            plc = new MelsecMcNet(ip_plc, port_plc);
            plc.ConnectTimeOut = 10000;
            plc.ReceiveTimeOut = 10000;

            var connect = plc.ConnectServer();
            if (!connect.IsSuccess)
            {
                MessageBox.Show("Không thể kết nối PLC: " + connect.Message);
                plc = null;
                return;
            }

            // Tạo timer mới
            plcTimer_Read = new System.Windows.Forms.Timer();
            plcTimer_Read.Interval = 1 * 1000; // 1 giây
            plcTimer_Read.Tick += PlcTimer_Read_Tick;
            plcTimer_Read.Start();

            // Đọc lần đầu
            ReadPLCAndUpdateUI();
        }

        // ==================== Hàm đọc PLC và update UI ====================
        private void ReadPLCAndUpdateUI()
        {
            if (plc == null) return; // chưa kết nối

            // Khai báo biến
            string cv_speed = "";
            string setting_weight = "";
            string weight_upper_limit_line = "";
            string weight_lower_limit_line = "";
            string setting_width = "";
            string width_upper_limit = "";
            string width_lower_limit = "";
            string actual_weight = "";
            string actual_width = "";
            DateTime update_time = DateTime.Now;

            foreach (string addr in addresses_plc_get)
            {
                if (addr.StartsWith("D") || addr.StartsWith("W"))
                {
                    var readResult = plc.ReadInt16(addr);
                    if (!readResult.IsSuccess) continue;

                    switch (addr)
                    {
                        case "D100": cv_speed = (readResult.Content/100).ToString(); break;
                        case "W30": setting_weight = (readResult.Content/10).ToString(); break;
                        case "W2D": setting_width = (readResult.Content/10).ToString(); break;
                        case "D330": weight_upper_limit_line = (readResult.Content / 100).ToString(); break;
                        case "D332": weight_lower_limit_line = (readResult.Content / 100).ToString(); break;
                        case "D334": width_upper_limit = (readResult.Content / 10).ToString(); break;
                        case "D336": width_lower_limit = (readResult.Content / 10).ToString(); break;
                        case "W124": actual_weight = (readResult.Content / 10).ToString(); break;
                        case "W40": actual_width = (readResult.Content / 10).ToString(); break;
                    }
                }
            }

            // Update UI
            cv_speed_view.Text = cv_speed;
            setting_weight_view.Text = setting_weight;
            weight_upper_limit_view.Text = weight_upper_limit_line;
            weight_lower_limit_view.Text = weight_lower_limit_line;
            setting_width_view.Text = setting_width;
            width_upper_limit_view.Text = width_upper_limit;
            width_lower_limit_view.Text = width_lower_limit;
            actual_weight_view.Text = actual_weight;
            actual_width_view.Text = actual_width;
            update_and_time_view.Text = update_time.ToString("yyyy-MM-dd HH:mm:ss");
            var a = double.Parse(weight_upper_limit_line);
            var b = double.Parse(weight_lower_limit_line);
            var c = double.Parse(setting_weight);
            var d = double.Parse(cv_speed);
            var e = double.Parse(actual_weight);
            var f = update_time;
            // autoscale (nếu muốn)
            chart_extruder.Plot.Axes.AutoScale();
            // refresh để cập nhật
            chart_extruder.Refresh();
            update_chart(a, b, c, d, e, f);
        }

        // ==================== Event Tick của Timer ====================
        private void PlcTimer_Read_Tick(object? sender, EventArgs e)
        {
            ReadPLCAndUpdateUI();
        }

        // ==================== Event click nút ghi dữ liệu ====================
        private void plc_write_Click(object? sender, EventArgs e)
        {
            try
            {
                // Ghi dữ liệu hiện tại vào DB
                InsertDataToDatabase();

                // Khởi tạo timer nếu chưa có
                if (plcTimer_Write == null)
                {
                    plcTimer_Write = new System.Windows.Forms.Timer();
                    plcTimer_Write.Interval = 60 * 1000; // 60 giây
                    plcTimer_Write.Tick += PlcTimer_Write_Tick;
                    plcTimer_Write.Start();
                }
                else
                {
                    // Nếu timer đã tồn tại, reset lại
                    plcTimer_Write.Stop();
                    plcTimer_Write.Start();
                }

                MessageBox.Show("Đã bắt đầu tự động ghi dữ liệu mỗi 1 phút.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi ghi dữ liệu: " + ex.Message);
            }
        }

        // ==================== Hàm Timer ghi dữ liệu tự động ====================
        private void PlcTimer_Write_Tick(object? sender, EventArgs e)
        {
            try
            {
                InsertDataToDatabase();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi cập nhật dữ liệu tự động: " + ex.Message);
            }
        }

        // ==================== Hàm ghi dữ liệu vào Database ====================
        private void InsertDataToDatabase()
        {
            string connString = $"Host={ip_server};Port={port};Username={user};Password={pass};Database={database_name}";

            using (var conn = new NpgsqlConnection(connString))
            {
                conn.Open();

                string sql = @"
            INSERT INTO bc_tread_extruding_logging 
            (size, pattern, type, mascot_name, kind, setting_weigth, setting_width, 
             weight_upper_limit, weight_lower_limit, width_upper_limit, width_lower_limit, 
             cv_speed, actual_weight, actual_width, entry_day_time, weight_deviation_setting, management_no)
            VALUES 
            (@size, @pattern, @type, @mascot_name, @kind, @setting_weigth, @setting_width,
             @weight_upper_limit, @weight_lower_limit, @width_upper_limit, @width_lower_limit,
             @cv_speed, @actual_weight, @actual_width, @entry_day_time, @weight_deviation_setting, @management_no);
        ";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("size", size_view.Text);
                    cmd.Parameters.AddWithValue("pattern", pattern_view.Text);
                    cmd.Parameters.AddWithValue("type", type_view.Text);
                    cmd.Parameters.AddWithValue("mascot_name", mascot_name_view.Text);
                    cmd.Parameters.AddWithValue("kind", kind_view.Text);
                    cmd.Parameters.AddWithValue("setting_weigth", double.Parse(setting_weight_view.Text));
                    cmd.Parameters.AddWithValue("setting_width", double.Parse(setting_width_view.Text));
                    cmd.Parameters.AddWithValue("weight_upper_limit", double.Parse(weight_upper_limit_view.Text));
                    cmd.Parameters.AddWithValue("weight_lower_limit", double.Parse(weight_lower_limit_view.Text));
                    cmd.Parameters.AddWithValue("width_upper_limit", double.Parse(width_upper_limit_view.Text));
                    cmd.Parameters.AddWithValue("width_lower_limit", double.Parse(width_lower_limit_view.Text));
                    cmd.Parameters.AddWithValue("cv_speed", double.Parse(cv_speed_view.Text));
                    cmd.Parameters.AddWithValue("actual_weight", double.Parse(actual_weight_view.Text));
                    cmd.Parameters.AddWithValue("actual_width", double.Parse(actual_width_view.Text));
                    cmd.Parameters.AddWithValue("entry_day_time", DateTime.Now); // thời gian hiện tại
                    cmd.Parameters.AddWithValue("weight_deviation_setting", 0);
                    cmd.Parameters.AddWithValue("management_no", management_no_view.Text);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ==================== Event click nút đóng ====================
        private void close_Click(object? sender, EventArgs e)
        {
            Close();
            Application.Exit();
        }

    }
}
