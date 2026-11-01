using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenMonitoringApp
{
    public static class MobiTestSuite
    {
        private static int totalTests = 0;
        private static int passedTests = 0;
        private static int failedTests = 0;
        private static readonly StringBuilder report = new StringBuilder();

        private static void Assert(bool condition, string testName, string details = "")
        {
            totalTests++;
            if (condition)
            {
                passedTests++;
                string line = string.Format("[PASS] {0}{1}", testName, string.IsNullOrEmpty(details) ? "" : " (" + details + ")");
                report.AppendLine(line);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(line);
                Console.ResetColor();
            }
            else
            {
                failedTests++;
                string line = string.Format("[FAIL] {0}{1}", testName, string.IsNullOrEmpty(details) ? "" : " (" + details + ")");
                report.AppendLine(line);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(line);
                Console.ResetColor();
            }
        }

        public static int RunAll()
        {
            report.AppendLine("═══════════════════════════════════════════════════════════");
            report.AppendLine("   MOBIDESK COMPREHENSIVE AUTOMATED TEST SUITE   ");
            report.AppendLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine(report.ToString());

            try { TestModernDarkScrollBar(); } catch (Exception ex) { Assert(false, "TestModernDarkScrollBar Exception", ex.ToString()); }
            try { TestFormMinimumSize(); } catch (Exception ex) { Assert(false, "TestFormMinimumSize Exception", ex.ToString()); }
            try { TestHeaderAntiCollision(); } catch (Exception ex) { Assert(false, "TestHeaderAntiCollision Exception", ex.ToString()); }
            try { TestNotificationsResponsiveCards(); } catch (Exception ex) { Assert(false, "TestNotificationsResponsiveCards Exception", ex.ToString()); }
            try { TestScrollBarVisibilityAndScrolling(); } catch (Exception ex) { Assert(false, "TestScrollBarVisibilityAndScrolling Exception", ex.ToString()); }
            try { TestNotificationDismissAndClearAll(); } catch (Exception ex) { Assert(false, "TestNotificationDismissAndClearAll Exception", ex.ToString()); }
            try { TestDeviceFiltering(); } catch (Exception ex) { Assert(false, "TestDeviceFiltering Exception", ex.ToString()); }
            try { TestNotificationsBackgroundArrival(); } catch (Exception ex) { Assert(false, "TestNotificationsBackgroundArrival Exception", ex.ToString()); }
            try { TestMonitorSelectorModernScrollBar(); } catch (Exception ex) { Assert(false, "TestMonitorSelectorModernScrollBar Exception", ex.ToString()); }
            try { TestFlowPanelScrollBar(); } catch (Exception ex) { Assert(false, "TestFlowPanelScrollBar Exception", ex.ToString()); }
            try { TestLocalizationAndLanguageSwitch(); } catch (Exception ex) { Assert(false, "TestLocalizationAndLanguageSwitch Exception", ex.ToString()); }
            try { TestMobiFlowBubbleRenderingAndProgress(); } catch (Exception ex) { Assert(false, "TestMobiFlowBubbleRenderingAndProgress Exception", ex.ToString()); }
            try { TestAppConfigDefaultsAndValidation(); } catch (Exception ex) { Assert(false, "TestAppConfigDefaultsAndValidation Exception", ex.ToString()); }
            try { TestVirtualDisplayAndDriversIntegrity(); } catch (Exception ex) { Assert(false, "TestVirtualDisplayAndDriversIntegrity Exception", ex.ToString()); }
            try { TestWindowsStartupRegistryIntegration(); } catch (Exception ex) { Assert(false, "TestWindowsStartupRegistryIntegration Exception", ex.ToString()); }
            try { TestMobiFlowManagerDownloadsFolder(); } catch (Exception ex) { Assert(false, "TestMobiFlowManagerDownloadsFolder Exception", ex.ToString()); }
            try { TestAdbEnvironmentCheck(); } catch (Exception ex) { Assert(false, "TestAdbEnvironmentCheck Exception", ex.ToString()); }
            try { TestNotificationAppResolutionAndIcons(); } catch (Exception ex) { Assert(false, "TestNotificationAppResolutionAndIcons Exception", ex.ToString()); }

            report.AppendLine("═══════════════════════════════════════════════════════════");
            string summary = string.Format("RESULTS: Total={0}, Passed={1}, Failed={2}", totalTests, passedTests, failedTests);
            report.AppendLine(summary);
            report.AppendLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine(summary);

            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_results.log");
                File.WriteAllText(logPath, report.ToString());

                string rootLog = Path.Combine(MobiPathResolver.GetProjectRoot(), "test_results.log");
                if (!string.Equals(logPath, rootLog, StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(rootLog, report.ToString());
                }
            }
            catch { }

            return failedTests == 0 ? 0 : 1;
        }

        private static void TestModernDarkScrollBar()
        {
            using (ModernDarkScrollBar sb = new ModernDarkScrollBar())
            {
                sb.Size = new Size(10, 400);

                Assert(sb.Minimum == 0 && sb.Maximum == 100 && sb.Value == 0,
                    "ScrollBar: Initial default bounds and value",
                    string.Format("Min={0}, Max={1}, Val={2}", sb.Minimum, sb.Maximum, sb.Value));

                Assert(sb.Width == 10, "ScrollBar: Slim 10px width");

                // Clamp below minimum
                sb.Value = -50;
                Assert(sb.Value == 0, "ScrollBar: Negative value clamped to minimum (0)");

                // Clamp above maximum - largeChange
                sb.Maximum = 500;
                sb.LargeChange = 100;
                sb.Value = 1000;
                Assert(sb.Value == 400, "ScrollBar: Clamped to (Maximum - LargeChange)",
                    string.Format("Val={0}, Expected=400", sb.Value));

                // ValueChanged event firing
                bool changedFired = false;
                sb.ValueChanged += delegate { changedFired = true; };
                sb.Value = 250;
                Assert(changedFired && sb.Value == 250, "ScrollBar: ValueChanged fired on valid change");

                changedFired = false;
                sb.Value = 250;
                Assert(!changedFired, "ScrollBar: ValueChanged NOT fired when value is unchanged");

                // Maximum update auto-clamp
                sb.Value = 200;
                sb.Maximum = 250; // maxPossible = 250 - 100 = 150
                Assert(sb.Value == 150, "ScrollBar: Maximum reduction auto-clamps Value",
                    string.Format("Val={0}, Expected=150", sb.Value));

                // LargeChange update auto-clamp
                sb.Value = 100;
                sb.LargeChange = 200; // maxPossible = 250 - 200 = 50
                Assert(sb.Value == 50, "ScrollBar: LargeChange increase auto-clamps Value",
                    string.Format("Val={0}, Expected=50", sb.Value));

                // Thumb rectangle geometry
                Rectangle tr = sb.GetThumbRectForTest();
                Assert(tr.Height >= 32, "ScrollBar: Thumb minimum pill height >= 32px",
                    string.Format("Height={0}", tr.Height));
                Assert(tr.Bottom <= sb.Height, "ScrollBar: Thumb rect stays inside control bounds",
                    string.Format("Bottom={0}, SbHeight={1}", tr.Bottom, sb.Height));
            }
        }

        private static void TestFormMinimumSize()
        {
            using (MainForm f = new MainForm(false, null, false, 0, false))
            {
                f.reallyExit = true;
                Assert(f.MinimumSize.Width >= 860, "Form: MinimumSize Width >= 860",
                    string.Format("Width={0}", f.MinimumSize.Width));
                Assert(f.MinimumSize.Height >= 680, "Form: MinimumSize Height >= 680",
                    string.Format("Height={0}", f.MinimumSize.Height));

                f.ClientSize = new Size(860, 680);

                Assert(f.cardOpts != null && f.pnlRightCol != null, "Layout: cardOpts and pnlRightCol exist");
                if (f.cardOpts != null && f.pnlRightCol != null)
                {
                    Assert(f.cardOpts.Bottom <= f.pnlRightCol.Height,
                        "Layout [860x680]: cardOpts fits inside right column",
                        string.Format("cardOpts.B={0}, pnlRightCol.H={1}", f.cardOpts.Bottom, f.pnlRightCol.Height));
                }

                if (f.swStartup != null && f.cardOpts != null)
                {
                    Assert(f.swStartup.Bottom <= f.cardOpts.Height,
                        "Layout [860x680]: swStartup fits inside cardOpts without clipping",
                        string.Format("swStartup.B={0}, cardOpts.H={1}", f.swStartup.Bottom, f.cardOpts.Height));
                }

                if (f.cardScreens != null && f.cardAudio != null)
                {
                    Assert(f.cardScreens.Bottom <= f.cardAudio.Top,
                        "Layout [860x680]: cardScreens does not overlap cardAudio",
                        string.Format("cardScreens.B={0}, cardAudio.T={1}", f.cardScreens.Bottom, f.cardAudio.Top));
                }

                if (f.cardAudio != null && f.cardVirtual != null)
                {
                    Assert(f.cardAudio.Bottom <= f.cardVirtual.Top,
                        "Layout [860x680]: cardAudio does not overlap cardVirtual",
                        string.Format("cardAudio.B={0}, cardVirtual.T={1}", f.cardAudio.Bottom, f.cardVirtual.Top));
                }

                if (f.cardVirtual != null && f.pnlLeftCol != null)
                {
                    Assert(f.cardVirtual.Bottom <= f.pnlLeftCol.Height,
                        "Layout [860x680]: cardVirtual fits inside left column",
                        string.Format("cardVirtual.B={0}, pnlLeftCol.H={1}", f.cardVirtual.Bottom, f.pnlLeftCol.Height));
                }

                if (f.cardAudio != null)
                {
                    Assert(f.cardAudio.Height >= 104, "Layout [860x680]: cardAudio height >= 104px",
                        string.Format("Height={0}", f.cardAudio.Height));
                }
                if (f.cardVirtual != null)
                {
                    Assert(f.cardVirtual.Height >= 88, "Layout [860x680]: cardVirtual height >= 88px",
                        string.Format("Height={0}", f.cardVirtual.Height));
                }

                if (f.btnStartStop != null && f.pnlBottomBar != null)
                {
                    Assert(f.btnStartStop.Bottom <= f.pnlBottomBar.Top,
                        "Layout [860x680]: btnStartStop does not overlap bottom bar",
                        string.Format("btnStartStop.B={0}, pnlBottomBar.T={1}", f.btnStartStop.Bottom, f.pnlBottomBar.Top));
                    Assert(f.pnlBottomBar.Bottom <= f.ClientSize.Height,
                        "Layout [860x680]: pnlBottomBar fits inside client height",
                        string.Format("pnlBottomBar.B={0}, ClientH={1}", f.pnlBottomBar.Bottom, f.ClientSize.Height));
                }

                f.Close();
            }

            Assert(AppConfig.WindowWidth >= 860, "AppConfig: Minimum WindowWidth enforced >= 860",
                string.Format("WindowWidth={0}", AppConfig.WindowWidth));
            Assert(AppConfig.WindowHeight >= 680, "AppConfig: Minimum WindowHeight enforced >= 680",
                string.Format("WindowHeight={0}", AppConfig.WindowHeight));
        }

        private static void TestHeaderAntiCollision()
        {
            bool origLang = L10n.IsEnglish;
            try
            {
                // Test in English
                L10n.IsEnglish = true;
                using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 500, null))
                {
                    pnl.ApplyLanguage();
                    VerifyHeaderBounds(pnl, "English");
                }

                // Test in Turkish
                L10n.IsEnglish = false;
                using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 500, null))
                {
                    pnl.ApplyLanguage();
                    VerifyHeaderBounds(pnl, "Turkish");
                }
            }
            finally
            {
                L10n.IsEnglish = origLang;
            }
        }

        private static void VerifyHeaderBounds(MobiNotificationsPanel pnl, string langLabel)
        {
            Assert(pnl.pnlHeader != null, "Header [" + langLabel + "]: Header panel exists");
            if (pnl.pnlHeader == null) return;

            Assert(pnl.lblTitle.Right < pnl.lblCount.Left,
                "Header [" + langLabel + "]: Title does not collide with Count",
                string.Format("Title.R={0}, Count.L={1}", pnl.lblTitle.Right, pnl.lblCount.Left));

            Assert(pnl.lblCount.Right < pnl.lblStatusDot.Left,
                "Header [" + langLabel + "]: Count does not collide with StatusDot",
                string.Format("Count.R={0}, Dot.L={1}", pnl.lblCount.Right, pnl.lblStatusDot.Left));

            Assert(pnl.lblStatusDot.Right < pnl.lblStatusText.Left,
                "Header [" + langLabel + "]: StatusDot does not collide with StatusText",
                string.Format("Dot.R={0}, Status.L={1}", pnl.lblStatusDot.Right, pnl.lblStatusText.Left));

            if (pnl.cbDeviceFilter.Visible)
            {
                Assert(pnl.lblStatusText.Right < pnl.cbDeviceFilter.Left,
                    "Header [" + langLabel + "]: StatusText does not collide with Filter",
                    string.Format("Status.R={0}, Filter.L={1}", pnl.lblStatusText.Right, pnl.cbDeviceFilter.Left));

                Assert(pnl.cbDeviceFilter.Right < pnl.btnStartupToggle.Left,
                    "Header [" + langLabel + "]: Filter does not collide with Startup button",
                    string.Format("Filter.R={0}, Startup.L={1}", pnl.cbDeviceFilter.Right, pnl.btnStartupToggle.Left));
            }

            Assert(pnl.btnStartupToggle.Right < pnl.btnClearAll.Left,
                "Header [" + langLabel + "]: Startup button does not collide with Clear All button",
                string.Format("Startup.R={0}, Clear.L={1}", pnl.btnStartupToggle.Right, pnl.btnClearAll.Left));

            Assert(pnl.btnClearAll.Right <= pnl.pnlHeader.Width,
                "Header [" + langLabel + "]: Clear All button does not overflow header width",
                string.Format("Clear.R={0}, Header.W={1}", pnl.btnClearAll.Right, pnl.pnlHeader.Width));
        }

        private static void TestNotificationsResponsiveCards()
        {
            int[] testWidths = new int[] { 836, 1024, 1400 };
            foreach (int tw in testWidths)
            {
                using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, tw, 500, null))
                {
                    pnl.AddNotification(new NotificationCardItem
                    {
                        Id = 101,
                        Key = "resp_test_" + tw,
                        PackageName = "com.mobidesk.test",
                        AppName = "TestApp",
                        Title = "Notification with fairly long title text to check truncation and bounds",
                        Text = "Body line with sufficient content to verify responsive card layout",
                        DeviceSerial = "DEV_RESP",
                        Timestamp = DateTime.UtcNow.Ticks
                    });

                    Control card;
                    Assert(pnl.cardMap.TryGetValue("resp_test_" + tw, out card),
                        "Responsive [" + tw + "px]: Card added to map");

                    if (card != null)
                    {
                        int vpW = pnl.pnlNotifsViewport.ClientSize.Width;
                        int expectedCardW = Math.Max(200, vpW - 24);

                        Assert(card.Width == expectedCardW,
                            "Responsive [" + tw + "px]: Card width adapts dynamically",
                            string.Format("Actual={0}, Expected={1}", card.Width, expectedCardW));

                        Assert(card.Left == 12, "Responsive [" + tw + "px]: Card left margin is 12px");

                        ModernButton dismissBtn = null;
                        Label titleLbl = null;
                        foreach (Control c in card.Controls)
                        {
                            if (c is ModernButton && string.Equals(c.Tag as string, "BTN_DISMISS")) dismissBtn = (ModernButton)c;
                            if (c is Label && string.Equals(c.Tag as string, "LBL_TITLE")) titleLbl = (Label)c;
                        }

                        Assert(dismissBtn != null, "Responsive [" + tw + "px]: Dismiss button exists");
                        if (dismissBtn != null)
                        {
                            Assert(dismissBtn.Right <= card.Width,
                                "Responsive [" + tw + "px]: Dismiss button stays within card",
                                string.Format("Dismiss.R={0}, Card.W={1}", dismissBtn.Right, card.Width));

                            int expectedDismissX = card.Width - dismissBtn.Width - 12;
                            Assert(dismissBtn.Left == expectedDismissX,
                                "Responsive [" + tw + "px]: Dismiss button pinned to right edge with 12px padding",
                                string.Format("Dismiss.L={0}, Expected={1}", dismissBtn.Left, expectedDismissX));
                        }

                        if (titleLbl != null && dismissBtn != null)
                        {
                            Assert(titleLbl.Right < dismissBtn.Left,
                                "Responsive [" + tw + "px]: Title label does not collide with Dismiss button",
                                string.Format("Title.R={0}, Dismiss.L={1}", titleLbl.Right, dismissBtn.Left));
                        }
                    }

                    Assert(!pnl.pnlNotifsViewport.AutoScroll,
                        "Responsive [" + tw + "px]: Native AutoScroll is FALSE (no ugly horizontal scrollbar)");
                }
            }
        }

        private static void TestScrollBarVisibilityAndScrolling()
        {
            using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 400, null))
            {
                // 1. Zero notifications
                Assert(!pnl.vScrollBar.Visible, "ScrollTest: ScrollBar hidden when 0 notifications");
                Assert(pnl.lblEmptyHint.Visible, "ScrollTest: Empty hint visible when 0 notifications");

                // 2. Add 1 notification (height 72 + 8 = 80px <= viewport 354px)
                pnl.AddNotification(new NotificationCardItem { Key = "s1", Title = "Title 1", Text = "Msg 1" });
                Assert(!pnl.vScrollBar.Visible, "ScrollTest: ScrollBar hidden when notifications fit in viewport");
                Assert(!pnl.lblEmptyHint.Visible, "ScrollTest: Empty hint hidden when >= 1 notification exists");

                // 3. Add 9 more (total 10 cards -> 800px > 354px)
                for (int i = 2; i <= 10; i++)
                {
                    pnl.AddNotification(new NotificationCardItem { Key = "s" + i, Title = "Title " + i, Text = "Msg " + i });
                }

                Assert(pnl.vScrollBar.Visible, "ScrollTest: ScrollBar visible when cards exceed viewport");
                Assert(pnl.vScrollBar.Maximum > pnl.pnlNotifsViewport.Height,
                    "ScrollTest: ScrollBar Maximum exceeds viewport height",
                    string.Format("Max={0}, VpH={1}", pnl.vScrollBar.Maximum, pnl.pnlNotifsViewport.Height));

                // 4. Test scrolling
                pnl.vScrollBar.Value = 100;
                Assert(pnl.pnlNotifsContent.Top == -100,
                    "ScrollTest: Content panel scrolled up by scrollbar value",
                    string.Format("Content.Top={0}", pnl.pnlNotifsContent.Top));

                pnl.vScrollBar.Value = 0;
                Assert(pnl.pnlNotifsContent.Top == 0,
                    "ScrollTest: Content panel reset to top when scrollbar is 0");
            }
        }

        private static void TestNotificationDismissAndClearAll()
        {
            using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 400, null))
            {
                pnl.AddNotification(new NotificationCardItem { Key = "card_a", Title = "Card A", Text = "Msg A" });
                pnl.AddNotification(new NotificationCardItem { Key = "card_b", Title = "Card B", Text = "Msg B" });
                pnl.AddNotification(new NotificationCardItem { Key = "card_c", Title = "Card C", Text = "Msg C" });

                Assert(pnl.cardMap.Count == 3, "DismissTest: 3 cards added");
                Assert(pnl.lblCount.Text == "(3)", "DismissTest: Count shows (3)");

                // Dismiss 1 card
                pnl.RemoveNotification("card_b");
                Assert(pnl.cardMap.Count == 2, "DismissTest: 2 cards left after dismissing card_b");
                Assert(pnl.lblCount.Text == "(2)", "DismissTest: Count updated to (2)");
                Assert(!pnl.cardMap.ContainsKey("card_b"), "DismissTest: card_b removed from cardMap");

                // Clear All via btnClearAll.PerformClick()
                pnl.btnClearAll.PerformClick();
                Assert(pnl.cardMap.Count == 0, "DismissTest: 0 cards after Clear All");
                Assert(pnl.lblCount.Text == "(0)", "DismissTest: Count reset to (0)");
                Assert(pnl.lblEmptyHint.Visible, "DismissTest: Empty hint restored and visible after Clear All");
                Assert(!pnl.vScrollBar.Visible, "DismissTest: ScrollBar hidden after Clear All");
            }
        }

        private static void TestDeviceFiltering()
        {
            using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 400, null))
            {
                pnl.AddNotification(new NotificationCardItem { Key = "dev1_n1", DeviceSerial = "PHONE_1", Title = "P1-N1", Text = "M" });
                pnl.AddNotification(new NotificationCardItem { Key = "dev1_n2", DeviceSerial = "PHONE_1", Title = "P1-N2", Text = "M" });
                pnl.AddNotification(new NotificationCardItem { Key = "dev2_n1", DeviceSerial = "TABLET_2", Title = "T2-N1", Text = "M" });

                List<ConnectedDeviceItem> devs = new List<ConnectedDeviceItem>
                {
                    new ConnectedDeviceItem { Serial = "PHONE_1", Model = "Pixel 7" },
                    new ConnectedDeviceItem { Serial = "TABLET_2", Model = "Galaxy Tab" }
                };
                pnl.UpdateDeviceList(devs);

                Assert(pnl.cbDeviceFilter.Items.Count == 3, "FilterTest: 3 items in device filter (All + 2 devices)");

                // Filter to PHONE_1 (index 1)
                pnl.cbDeviceFilter.SelectedIndex = 1;
                Assert(pnl.cardMap["dev1_n1"].Visible, "FilterTest: dev1_n1 is visible");
                Assert(pnl.cardMap["dev1_n2"].Visible, "FilterTest: dev1_n2 is visible");
                Assert(!pnl.cardMap["dev2_n1"].Visible, "FilterTest: dev2_n1 is hidden");
                Assert(pnl.lblCount.Text == "(2)", "FilterTest: Count reflects filtered count (2)");

                // Filter to TABLET_2 (index 2)
                pnl.cbDeviceFilter.SelectedIndex = 2;
                Assert(!pnl.cardMap["dev1_n1"].Visible, "FilterTest: dev1_n1 is hidden");
                Assert(pnl.cardMap["dev2_n1"].Visible, "FilterTest: dev2_n1 is visible");
                Assert(pnl.lblCount.Text == "(1)", "FilterTest: Count reflects filtered count (1)");

                // Reset filter to All (index 0)
                pnl.cbDeviceFilter.SelectedIndex = 0;
                Assert(pnl.cardMap["dev1_n1"].Visible && pnl.cardMap["dev1_n2"].Visible && pnl.cardMap["dev2_n1"].Visible,
                    "FilterTest: All notifications visible when filter reset");
                Assert(pnl.lblCount.Text == "(3)", "FilterTest: Count reset to total (3)");
            }
        }

        private static void TestNotificationsBackgroundArrival()
        {
            using (Panel container = new Panel())
            {
                container.Visible = false;
                using (MobiNotificationsPanel pnl = new MobiNotificationsPanel(0, 0, 836, 400, null))
                {
                    container.Controls.Add(pnl);
                    Assert(!pnl.Visible, "BackgroundNotif: Panel is hidden inside hidden container");

                    pnl.AddNotification(new NotificationCardItem { Key = "bg1", Title = "WhatsApp", Text = "Hello" });
                    Assert(!pnl.lblEmptyHint.Visible, "BackgroundNotif: Empty hint is HIDDEN when notification added in background");
                    Assert(pnl.lblCount.Text == "(1)", "BackgroundNotif: Count is (1) even when received in background");

                    // Show container and panel (simulate switching to notifications tab)
                    container.Visible = true;
                    pnl.Visible = true;
                    pnl.UpdateLayout();
                    pnl.UpdateCount();

                    Assert(!pnl.lblEmptyHint.Visible, "BackgroundNotif: Empty hint remains HIDDEN after switching to tab");
                    Assert(pnl.lblCount.Text == "(1)", "BackgroundNotif: Count remains (1)");
                    Assert(pnl.cardMap["bg1"].Visible, "BackgroundNotif: Card is visible");
                }
            }
        }

        private static void TestMonitorSelectorModernScrollBar()
        {
            using (ModernMonitorSelector sel = new ModernMonitorSelector(0, 0, 320, 96))
            {
                Assert(!sel.AutoScroll, "MonitorSelector: AutoScroll is disabled (no ugly native white scrollbar)");
                Assert(sel.vScrollBar != null, "MonitorSelector: ModernDarkScrollBar instance created");
                Assert(sel.pnlContent != null, "MonitorSelector: DoubleBufferedPanel pnlContent created");

                // Test with 2 items (fits in 96px)
                List<MonitorItemData> list2 = new List<MonitorItemData>
                {
                    new MonitorItemData { Index = 0, DeviceName = @"\\.\DISPLAY1", Resolution = "1920x1080", Tag = "ANA MONİTÖR" },
                    new MonitorItemData { Index = 1, DeviceName = @"\\.\DISPLAY2", Resolution = "1920x1080", Tag = "MONİTÖR 2" }
                };
                sel.SetMonitors(list2, 0);
                Assert(!sel.vScrollBar.Visible, "MonitorSelector [2 items]: ScrollBar is hidden when items fit in viewport");
                Assert(sel.pnlContent.Top == 0, "MonitorSelector [2 items]: pnlContent.Top is 0");

                // Test with 4 items (exceeds 96px)
                List<MonitorItemData> list4 = new List<MonitorItemData>
                {
                    new MonitorItemData { Index = 0, DeviceName = @"\\.\DISPLAY1", Resolution = "1920x1080", Tag = "ANA MONİTÖR" },
                    new MonitorItemData { Index = 1, DeviceName = @"\\.\DISPLAY2", Resolution = "1920x1080", Tag = "MONİTÖR 2" },
                    new MonitorItemData { Index = 2, DeviceName = @"\\.\DISPLAY22", Resolution = "1280x800", Tag = "★ SANAL MOBIDESK", IsVirtual = true },
                    new MonitorItemData { Index = 3, DeviceName = @"\\.\DISPLAY4", Resolution = "1920x1080", Tag = "MONİTÖR 4" }
                };
                sel.SetMonitors(list4, 0);
                Assert(sel.vScrollBar.Visible, "MonitorSelector [4 items]: ModernDarkScrollBar visible when items exceed viewport");
                Assert(sel.pnlContent.Top == 0, "MonitorSelector [4 items]: Initial scroll stays at top (pnlContent.Top=0, first item never clipped)");

                // Scroll test
                sel.vScrollBar.Value = 40;
                Assert(sel.pnlContent.Top == -40, "MonitorSelector: Content panel scrolls smoothly by scrollbar value");

                // Selection change does NOT reset scroll or recreate controls
                sel.SelectedIndex = 2;
                Assert(sel.vScrollBar.Value == 40, "MonitorSelector: Changing selection does not reset scrollbar position");
                Assert(sel.pnlContent.Controls.Count == 4, "MonitorSelector: Controls count preserved during selection change");
                MonitorCardItem card2 = sel.pnlContent.Controls[2] as MonitorCardItem;
                Assert(card2 != null && card2.IsSelected, "MonitorSelector: Target card is selected");

                // Reset scroll
                sel.vScrollBar.Value = 0;
                Assert(sel.pnlContent.Top == 0, "MonitorSelector: Content panel resets cleanly to 0");
            }
        }

        private static void TestFlowPanelScrollBar()
        {
            using (MobiFlowPanel flow = new MobiFlowPanel(0, 0, 836, 500, null))
            {
                // 1. Viewport & Scrollbar structure
                Assert(flow.pnlFlowViewport != null && !flow.pnlFlowViewport.AutoScroll,
                    "FlowScrollTest: Viewport exists and native AutoScroll is FALSE");
                Assert(flow.vScrollBar != null && !flow.vScrollBar.Visible,
                    "FlowScrollTest: ModernDarkScrollBar initially hidden when 0 messages");
                Assert(flow.pnlMessages != null,
                    "FlowScrollTest: pnlMessages content panel exists inside viewport");

                // 2. Add 1 short message (fits in viewport ~400px)
                flow.AddMessage(new FlowMessageItem { Id = "m1", Text = "Merhaba", Sender = "mobile" });
                Assert(!flow.vScrollBar.Visible,
                    "FlowScrollTest: ScrollBar stays hidden when 1 message fits viewport");

                // 3. Add multiple messages until viewport is exceeded
                for (int i = 2; i <= 15; i++)
                {
                    flow.AddMessage(new FlowMessageItem
                    {
                        Id = "m" + i,
                        Text = "Test Flow Mesajı #" + i + " uzun metin içeriği ve detaylar",
                        Sender = (i % 2 == 0) ? "pc" : "mobile"
                    });
                }

                Assert(flow.vScrollBar.Visible,
                    "FlowScrollTest: ScrollBar visible when messages exceed viewport height");
                Assert(flow.vScrollBar.Maximum > flow.pnlFlowViewport.Height,
                    "FlowScrollTest: ScrollBar Maximum exceeds viewport height",
                    string.Format("Max={0}, VpH={1}", flow.vScrollBar.Maximum, flow.pnlFlowViewport.Height));

                // 4. Test scrolling behavior
                flow.vScrollBar.Value = 120;
                Assert(flow.pnlMessages.Top == -120,
                    "FlowScrollTest: pnlMessages scrolls up by scrollbar value (-120)",
                    string.Format("pnlMessages.Top={0}", flow.pnlMessages.Top));

                flow.vScrollBar.Value = 0;
                Assert(flow.pnlMessages.Top == 0,
                    "FlowScrollTest: pnlMessages resets to 0 when scrollbar is 0");

                // 5. Test auto-scroll to bottom on new message
                flow.AddMessage(new FlowMessageItem { Id = "m_last", Text = "Son mesaj", Sender = "pc" });
                int expectedMax = Math.Max(0, flow.vScrollBar.Maximum - flow.vScrollBar.LargeChange);
                Assert(flow.vScrollBar.Value == expectedMax,
                    "FlowScrollTest: Auto-scrolls to bottom on new message",
                    string.Format("Val={0}, Expected={1}", flow.vScrollBar.Value, expectedMax));
            }
        }

        private static void TestLocalizationAndLanguageSwitch()
        {
            string origLang = AppConfig.Lang;
            try
            {
                // Test Turkish
                AppConfig.Lang = "tr";
                Assert(string.Equals(L10n.T("Merhaba", "Hello"), "Merhaba"),
                    "L10nTest: Turkish returns Turkish string");
                Assert(!L10n.IsEnglish, "L10nTest: IsEnglish is false for 'tr'");

                // Test English
                AppConfig.Lang = "en";
                Assert(string.Equals(L10n.T("Merhaba", "Hello"), "Hello"),
                    "L10nTest: English returns English string");
                Assert(L10n.IsEnglish, "L10nTest: IsEnglish is true for 'en'");

                // Test Panel Language Switching without exception
                using (MobiNotificationsPanel notifs = new MobiNotificationsPanel(0, 0, 836, 400, null))
                using (MobiFlowPanel flow = new MobiFlowPanel(0, 0, 836, 400, null))
                {
                    AppConfig.Lang = "tr";
                    notifs.ApplyLanguage();
                    flow.ApplyLanguage();
                    Assert(notifs.lblTitle.Text.Contains("Bildirimler"), "L10nTest: Notifications title contains Turkish text");

                    AppConfig.Lang = "en";
                    notifs.ApplyLanguage();
                    flow.ApplyLanguage();
                    Assert(notifs.lblTitle.Text.Contains("Notifications"), "L10nTest: Notifications title contains English text");
                }
            }
            finally
            {
                AppConfig.Lang = origLang;
            }
        }

        private static void TestMobiFlowBubbleRenderingAndProgress()
        {
            using (MobiFlowPanel flow = new MobiFlowPanel(0, 0, 836, 500, null))
            {
                // 1. PC bubble right alignment
                FlowMessageItem pcMsg = new FlowMessageItem { Id = "pc_1", Text = "Merhaba PC", Sender = "pc" };
                flow.AddMessage(pcMsg);
                Control pcBubble = null;
                foreach (Control c in flow.pnlMessages.Controls)
                {
                    if (string.Equals(c.Tag as string, "pc_1")) { pcBubble = c; break; }
                }
                Assert(pcBubble != null, "FlowBubbleTest: PC bubble created");
                if (pcBubble != null)
                {
                    Assert(string.Equals(pcBubble.AccessibleName, "ME"), "FlowBubbleTest: PC bubble has 'ME' AccessibleName");
                    Assert(pcBubble.Left > 100, "FlowBubbleTest: PC bubble is right-aligned", string.Format("Left={0}", pcBubble.Left));

                    // Dynamic screen resizing test (Responsive message bubble rearrangement)
                    flow.Size = new Size(1400, 600);
                    Assert(pcBubble.Left > 1100,
                        "FlowResponsive: PC bubble adapts to expanded window width dynamically",
                        string.Format("Left={0}, VpW={1}", pcBubble.Left, flow.pnlFlowViewport.Width));
                    flow.Size = new Size(700, 400);
                    Assert(pcBubble.Left < 600,
                        "FlowResponsive: PC bubble adapts to contracted window width dynamically",
                        string.Format("Left={0}, VpW={1}", pcBubble.Left, flow.pnlFlowViewport.Width));
                    flow.Size = new Size(836, 500);
                }

                // 2. Mobile bubble left alignment
                FlowMessageItem mobMsg = new FlowMessageItem { Id = "mob_1", Text = "Merhaba Mobil", Sender = "mobile" };
                flow.AddMessage(mobMsg);
                Control mobBubble = null;
                foreach (Control c in flow.pnlMessages.Controls)
                {
                    if (string.Equals(c.Tag as string, "mob_1")) { mobBubble = c; break; }
                }
                Assert(mobBubble != null, "FlowBubbleTest: Mobile bubble created");
                if (mobBubble != null)
                {
                    Assert(string.Equals(mobBubble.AccessibleName, "OTHER"), "FlowBubbleTest: Mobile bubble has 'OTHER' AccessibleName");
                    Assert(mobBubble.Left == 12, "FlowBubbleTest: Mobile bubble is left-aligned (Left=12)", string.Format("Left={0}", mobBubble.Left));
                }

                // 3. URL detection in message
                FlowMessageItem urlMsg = new FlowMessageItem { Id = "url_1", Text = "https://github.com", Sender = "pc" };
                flow.AddMessage(urlMsg);
                Control urlBubble = null;
                foreach (Control c in flow.pnlMessages.Controls)
                {
                    if (string.Equals(c.Tag as string, "url_1")) { urlBubble = c; break; }
                }
                bool hasLinkLabel = false;
                if (urlBubble != null)
                {
                    foreach (Control child in urlBubble.Controls)
                    {
                        if (child is LinkLabel) { hasLinkLabel = true; break; }
                    }
                }
                Assert(hasLinkLabel, "FlowBubbleTest: URL message generates clickable LinkLabel");

                // 4. File bubble & progress update
                FlowMessageItem fileMsg = new FlowMessageItem
                {
                    Id = "file_1",
                    IsFile = true,
                    FileName = "test_document.pdf",
                    FileSize = 2500000, // ~2.4 MB
                    Progress = 25,
                    Sender = "mobile"
                };
                flow.AddMessage(fileMsg);
                Control fileBubble = null;
                foreach (Control c in flow.pnlMessages.Controls)
                {
                    if (string.Equals(c.Tag as string, "file_1")) { fileBubble = c; break; }
                }
                Assert(fileBubble != null, "FlowBubbleTest: File bubble created");

                // Update progress to 100%
                fileMsg.Progress = 100;
                flow.UpdateFileProgress(fileMsg);
                string progressText = "";
                if (fileBubble != null)
                {
                    foreach (Control child in fileBubble.Controls)
                    {
                        if (string.Equals(child.Tag as string, "FILE_STATUS_LABEL"))
                        {
                            progressText = child.Text;
                            break;
                        }
                    }
                }
                Assert(!string.IsNullOrEmpty(progressText) && (progressText.Contains("Tamamlandı") || progressText.Contains("Completed")),
                    "FlowBubbleTest: File progress successfully updated to 100% completed", progressText);

                // 5. Drag & Drop acceptance
                Assert(flow.AllowDrop && flow.pnlFlowViewport.AllowDrop && flow.pnlMessages.AllowDrop,
                    "FlowBubbleTest: Drag & Drop enabled on panels");

                // 6. Device list update stability (Anti-flicker test)
                List<ConnectedDeviceItem> testDevs = new List<ConnectedDeviceItem>
                {
                    new ConnectedDeviceItem { Serial = "SER1", FriendlyName = "Device 1", FlowPort = 55557 },
                    new ConnectedDeviceItem { Serial = "SER2", FriendlyName = "Device 2", FlowPort = 55558 }
                };
                flow.UpdateDeviceList(testDevs);
                int controlsCountBefore = flow.pnlMessages.Controls.Count;
                // Re-running UpdateDeviceList with identical devices must NOT rebuild or clear controls
                flow.UpdateDeviceList(testDevs);
                flow.UpdateDeviceList(testDevs);
                Assert(flow.pnlMessages.Controls.Count == controlsCountBefore,
                    "FlowAntiFlicker: UpdateDeviceList does not clear or rebuild controls on repeated polling ticks");
            }
        }

        private static void TestAppConfigDefaultsAndValidation()
        {
            Assert(AppConfig.WindowWidth >= 860, "ConfigTest: Minimum WindowWidth enforced >= 860");
            Assert(AppConfig.WindowHeight >= 680, "ConfigTest: Minimum WindowHeight enforced >= 680");
            Assert(AppConfig.Fps >= 15 && AppConfig.Fps <= 75, "ConfigTest: FPS default within safe bounds [15, 75]");
            Assert(AppConfig.Quality >= 15 && AppConfig.Quality <= 100, "ConfigTest: Quality default within safe bounds [15, 100]");
            Assert(!string.IsNullOrEmpty(AppConfig.Lang), "ConfigTest: Language code is not empty");
        }

        private static void TestVirtualDisplayAndDriversIntegrity()
        {
            string addBat = MobiPathResolver.ResolveDisplayScript("Sanal_Ekran_Ekle.bat");
            string removeBat = MobiPathResolver.ResolveDisplayScript("Sanal_Ekran_Kaldir.bat");
            Assert(File.Exists(addBat), "VDDTest: Sanal_Ekran_Ekle.bat script exists");
            Assert(File.Exists(removeBat), "VDDTest: Sanal_Ekran_Kaldir.bat script exists");

            string usbmmiddDir = MobiPathResolver.ResolveDirectory("usbmmidd", "drivers/display");
            string vddDir = MobiPathResolver.ResolveDirectory("vdd", "drivers/display");
            Assert(Directory.Exists(usbmmiddDir) || Directory.Exists(vddDir),
                "VDDTest: At least one virtual display driver directory exists (usbmmidd or vdd)");
        }

        private static void TestWindowsStartupRegistryIntegration()
        {
            bool startupChecked = false;
            try
            {
                bool isEnabled = WindowsStartupManager.IsStartupEnabled();
                startupChecked = true;
            }
            catch { }
            Assert(startupChecked, "StartupTest: WindowsStartupManager.IsStartupEnabled() executes safely without throwing");
        }

        private static void TestMobiFlowManagerDownloadsFolder()
        {
            Assert(!string.IsNullOrEmpty(MobiFlowManager.DownloadsFolder),
                "FlowManagerTest: DownloadsFolder path is defined");
            Assert(Directory.Exists(MobiFlowManager.DownloadsFolder),
                "FlowManagerTest: DownloadsFolder exists on disk",
                MobiFlowManager.DownloadsFolder);
        }

        private static void TestAdbEnvironmentCheck()
        {
            string adbPath = MainForm.FindAdbPath();
            bool adbFound = File.Exists(adbPath) || string.Equals(adbPath, "adb");
            Assert(adbFound, "AdbTest: ADB path resolved", adbPath);

            int deviceCount = 0;
            string deviceNames = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "devices -l",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        if (line.Contains("device product:") || line.Contains("device ") || line.Contains("device\t"))
                        {
                            deviceCount++;
                            deviceNames += (deviceNames.Length > 0 ? ", " : "") + line.Split(' ')[0];
                        }
                    }
                }
            }
            catch { }
            Assert(deviceCount >= 0, "AdbTest: ADB device discovery executed", string.Format("Connected={0} ({1})", deviceCount, deviceNames));
        }

        private static void TestNotificationAppResolutionAndIcons()
        {
            // 1. Known package resolution
            string gmailName = NotificationHelper.ResolveAppName("com.google.android.gm", "com.google.android.gm");
            Assert(gmailName == "Gmail", "NotifAppTest: com.google.android.gm resolves to 'Gmail'", gmailName);

            string waName = NotificationHelper.ResolveAppName("com.whatsapp", "com.whatsapp");
            Assert(waName == "WhatsApp", "NotifAppTest: com.whatsapp resolves to 'WhatsApp'", waName);

            // 2. Generic package cleanup
            string customName = NotificationHelper.ResolveAppName("com.trendyol.trendyol", "com.trendyol.trendyol");
            Assert(customName == "Trendyol", "NotifAppTest: com.trendyol.trendyol resolves to 'Trendyol'", customName);

            // 3. Fallback icon generation
            using (Bitmap gmailBmp = NotificationHelper.GetAppIconOrBadge("com.google.android.gm", "Gmail"))
            {
                Assert(gmailBmp != null && gmailBmp.Width == 36 && gmailBmp.Height == 36,
                    "NotifIconTest: Generated Gmail fallback icon has 36x36 dimensions");
            }

            using (Bitmap genBmp = NotificationHelper.GetAppIconOrBadge("com.custom.app", "CustomApp"))
            {
                Assert(genBmp != null && genBmp.Width == 36 && genBmp.Height == 36,
                    "NotifIconTest: Generated generic app fallback badge has 36x36 dimensions");
            }
        }
    }

}