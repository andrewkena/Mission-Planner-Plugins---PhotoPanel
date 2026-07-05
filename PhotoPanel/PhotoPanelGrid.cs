// PhotoPanelGrid.cs — плагин Mission Planner "PhotoPanel (grid)" (для MP 1.3.83)
// Вкладка "Фото" на экране Flight Data (внизу слева, рядом с Quick/Actions/...),
// та же статистика по фотоснимкам, что и в PhotoPanelTab, но в виде компактной
// таблицы "параметр/значение" вместо колонки подписей.
// Установка: положить в C:\Program Files (x86)\Mission Planner\plugins\ и перезапустить MP.
// ВАЖНО: не держать в plugins одновременно с Photopanel.cs (вариант со списком меток) —
// классы конфликтуют по имени namespace/подпискам, оставь один файл.

using System;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner;
using MissionPlanner.Plugin;

namespace PhotoPanelGridPlugin
{
    public class PhotoPanelGrid : Plugin
    {
        private TabPage _tab;
        private DataGridView _grid;

        // счётчик и статистика фото
        private int _photoCount = 0;
        private double _lastLat = 0, _lastLng = 0;
        private double _lastAlt = 0;
        private DateTime _lastPhotoTime = DateTime.MinValue;
        private double _sumDtSec = 0;
        private double _sumDistM = 0;
        private int _intervals = 0;
        private double _vdop = -1;
        private object _lock = new object();

        // индексы строк в таблице
        private const int RowCount = 0;
        private const int RowTime = 1;
        private const int RowCoords = 2;
        private const int RowAlt = 3;
        private const int RowSpeed = 4;
        private const int RowSats = 5;
        private const int RowHdop = 6;
        private const int RowVdop = 7;
        private const int RowAvgTime = 8;
        private const int RowAvgDist = 9;
        private const int RowCurWp = 10;
        private const int RowWpDist = 11;

        public override string Name { get { return "Photo Panel Grid"; } }
        public override string Version { get { return "1.0"; } }
        public override string Author { get { return "Andrey"; } }

        public override bool Init()
        {
            loopratehz = 2f;
            return true;
        }

        public override bool Loaded()
        {
            try
            {
                // CAMERA_FEEDBACK — счётчик фото + статистика интервалов
                Host.comPort.SubscribeToPacketType(
                    MAVLink.MAVLINK_MSG_ID.CAMERA_FEEDBACK,
                    delegate(MAVLink.MAVLinkMessage message)
                    {
                        MAVLink.mavlink_camera_feedback_t fb =
                            (MAVLink.mavlink_camera_feedback_t)message.data;
                        double lat = fb.lat / 10000000.0;
                        double lng = fb.lng / 10000000.0;
                        DateTime now = DateTime.Now;

                        lock (_lock)
                        {
                            if (_photoCount > 0)
                            {
                                _sumDtSec += (now - _lastPhotoTime).TotalSeconds;
                                _sumDistM += DistM(_lastLat, _lastLng, lat, lng);
                                _intervals++;
                            }
                            _photoCount++;
                            _lastLat = lat;
                            _lastLng = lng;
                            _lastAlt = fb.alt_rel;
                            _lastPhotoTime = now;
                        }
                        return true;
                    },
                    (byte)Host.comPort.sysidcurrent,
                    (byte)Host.comPort.compidcurrent,
                    false);

                // GPS_RAW_INT — VDOP (epv)
                Host.comPort.SubscribeToPacketType(
                    MAVLink.MAVLINK_MSG_ID.GPS_RAW_INT,
                    delegate(MAVLink.MAVLinkMessage message)
                    {
                        MAVLink.mavlink_gps_raw_int_t gps =
                            (MAVLink.mavlink_gps_raw_int_t)message.data;
                        lock (_lock)
                        {
                            _vdop = (gps.epv == 65535) ? -1 : gps.epv / 100.0;
                        }
                        return true;
                    },
                    (byte)Host.comPort.sysidcurrent,
                    (byte)Host.comPort.compidcurrent,
                    false);

                // Вкладка создаётся в главном потоке
                MainV2.instance.BeginInvoke((MethodInvoker)delegate
                {
                    AddTab();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("PhotoPanelGrid Loaded error: " + ex.ToString());
            }

            return true;
        }

        public override bool Loop()
        {
            if (_tab == null || _tab.IsDisposed)
                return true;

            try
            {
                MainV2.instance.BeginInvoke((MethodInvoker)delegate { UpdateGrid(); });
            }
            catch (Exception) { }

            return true;
        }

        public override bool Exit()
        {
            return true;
        }

        // ---------- UI (только главный поток) ----------

        private void AddTab()
        {
            Control[] found = MainV2.instance.FlightData.Controls.Find("tabControlactions", true);
            if (found.Length == 0)
            {
                Console.WriteLine("PhotoPanelGrid: tabControlactions не найден");
                return;
            }
            TabControl tc = (TabControl)found[0];

            _tab = new TabPage("Фото");
            _tab.BackColor = Color.FromArgb(38, 39, 40);

            Button btnReset = new Button();
            btnReset.Text = "Сброс";
            btnReset.Dock = DockStyle.Top;
            btnReset.Height = 30;
            btnReset.FlatStyle = FlatStyle.Flat;
            btnReset.ForeColor = Color.WhiteSmoke;
            btnReset.Click += delegate
            {
                lock (_lock)
                {
                    _photoCount = 0;
                    _lastPhotoTime = DateTime.MinValue;
                    _lastAlt = 0;
                    _sumDtSec = 0;
                    _sumDistM = 0;
                    _intervals = 0;
                }
            };

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.BackgroundColor = Color.FromArgb(38, 39, 40);
            _grid.GridColor = Color.FromArgb(60, 61, 62);
            _grid.BorderStyle = BorderStyle.None;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.ReadOnly = true;
            _grid.RowHeadersVisible = false;
            _grid.ColumnHeadersVisible = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.ScrollBars = ScrollBars.Vertical;

            _grid.DefaultCellStyle.BackColor = Color.FromArgb(38, 39, 40);
            _grid.DefaultCellStyle.ForeColor = Color.WhiteSmoke;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(38, 39, 40);
            _grid.DefaultCellStyle.SelectionForeColor = Color.WhiteSmoke;
            _grid.DefaultCellStyle.Font = new Font("Segoe UI", 10);

            _grid.Columns.Add("param", "Параметр");
            _grid.Columns.Add("value", "Значение");
            _grid.Columns[0].Width = 220;
            _grid.Columns[1].Width = 130;
            _grid.Columns[0].DefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            string[] labels = new string[]
            {
                "Снимков сделано",
                "Время последнего фото",
                "Координаты последнего фото",
                "Высота последнего снимка",
                "Скорость",
                "Спутники",
                "HDOP",
                "VDOP",
                "Ср. время между фото",
                "Ср. расстояние между фото",
                "Текущий WP",
                "Расстояние до WP",
            };

            foreach (string label in labels)
                _grid.Rows.Add(label, "—");

            _grid.Rows[RowCount].DefaultCellStyle.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            _grid.Rows[RowCount].DefaultCellStyle.ForeColor = Color.LimeGreen;

            _tab.Controls.Add(_grid);
            _tab.Controls.Add(btnReset);

            tc.TabPages.Add(_tab);
        }

        private void UpdateGrid()
        {
            if (_grid == null) return;

            int count, intervals;
            double lat, lng, alt, sumDt, sumDist, vdop;
            DateTime t;
            lock (_lock)
            {
                count = _photoCount;
                lat = _lastLat;
                lng = _lastLng;
                alt = _lastAlt;
                t = _lastPhotoTime;
                sumDt = _sumDtSec;
                sumDist = _sumDistM;
                intervals = _intervals;
                vdop = _vdop;
            }

            CurrentState cs = Host.cs;

            SetValue(RowCount, count.ToString());

            if (t == DateTime.MinValue)
            {
                SetValue(RowTime, "—");
                SetValue(RowCoords, "—");
                SetValue(RowAlt, "—");
            }
            else
            {
                SetValue(RowTime, string.Format("{0:HH:mm:ss}", t));
                SetValue(RowCoords, string.Format("{0:F6}, {1:F6}", lat, lng));
                SetValue(RowAlt, string.Format("{0:F1} м", alt));
            }

            SetValue(RowSpeed, string.Format("{0:F1} м/с", cs.airspeed));
            SetValue(RowSats, cs.satcount.ToString());
            SetValue(RowHdop, string.Format("{0:F1}", cs.gpshdop));
            SetValue(RowVdop, (vdop < 0) ? "—" : vdop.ToString("F1"));

            if (intervals > 0)
            {
                SetValue(RowAvgTime, string.Format("{0:F1} с", sumDt / intervals));
                SetValue(RowAvgDist, string.Format("{0:F1} м", sumDist / intervals));
            }
            else
            {
                SetValue(RowAvgTime, "—");
                SetValue(RowAvgDist, "—");
            }

            SetValue(RowCurWp, cs.wpno.ToString());
            SetValue(RowWpDist, string.Format("{0:F0} м", cs.wp_dist));
        }

        private void SetValue(int row, string value)
        {
            _grid.Rows[row].Cells[1].Value = value;
        }

        // Расстояние между двумя точками (гаверсинус), метры
        private static double DistM(double lat1, double lon1, double lat2, double lon2)
        {
            double R = 6371000.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }
    }
}
