namespace FilmKiralama.UI
{
    partial class AdminPanel
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            this.btnAracDurum = new System.Windows.Forms.Button();
            this.btnKullaniciPanel = new System.Windows.Forms.Button();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.txtID = new System.Windows.Forms.TextBox();
            this.txtAd = new System.Windows.Forms.TextBox();
            this.txtTur = new System.Windows.Forms.TextBox();
            this.txtYonetmen = new System.Windows.Forms.TextBox();
            this.txtUcret = new System.Windows.Forms.TextBox();
            this.txtDurum = new System.Windows.Forms.TextBox();
            this.txtLink = new System.Windows.Forms.TextBox();
            this.btnFilmSil = new System.Windows.Forms.Button();
            this.btnGuncelle = new System.Windows.Forms.Button();
            this.btnFilmEkle = new System.Windows.Forms.Button();
            this.btnPanelCikis = new System.Windows.Forms.Button();
            this.errorProvider1 = new System.Windows.Forms.ErrorProvider(this.components);
            this.cmbYil = new System.Windows.Forms.ComboBox();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.btnKiralanlar = new System.Windows.Forms.Button();
            this.label8 = new System.Windows.Forms.Label();
            this.txtHakkinda = new System.Windows.Forms.TextBox();
            this.lblAdet = new System.Windows.Forms.Label();
            this.nudAdet = new System.Windows.Forms.NumericUpDown();
            this.chkKiralamayaAcik = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudAdet)).BeginInit();
            this.SuspendLayout();
            // 
            // btnAracDurum
            // 
            this.btnAracDurum.ForeColor = System.Drawing.Color.Black;
            this.btnAracDurum.Location = new System.Drawing.Point(16, 15);
            this.btnAracDurum.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnAracDurum.Name = "btnAracDurum";
            this.btnAracDurum.Size = new System.Drawing.Size(280, 64);
            this.btnAracDurum.TabIndex = 0;
            this.btnAracDurum.Text = "Film Durumu";
            this.btnAracDurum.UseVisualStyleBackColor = true;
            this.btnAracDurum.Click += new System.EventHandler(this.btnAracDurum_Click);
            // 
            // btnKullaniciPanel
            // 
            this.btnKullaniciPanel.ForeColor = System.Drawing.Color.Black;
            this.btnKullaniciPanel.Location = new System.Drawing.Point(304, 15);
            this.btnKullaniciPanel.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnKullaniciPanel.Name = "btnKullaniciPanel";
            this.btnKullaniciPanel.Size = new System.Drawing.Size(280, 64);
            this.btnKullaniciPanel.TabIndex = 1;
            this.btnKullaniciPanel.Text = "Kullanıcı Paneli";
            this.btnKullaniciPanel.UseVisualStyleBackColor = true;
            this.btnKullaniciPanel.Click += new System.EventHandler(this.btnKullaniciPanel_Click);
            // 
            // dataGridView1
            // 
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView1.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            dataGridViewCellStyle2.ForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dataGridView1.DefaultCellStyle = dataGridViewCellStyle2;
            this.dataGridView1.Location = new System.Drawing.Point(16, 86);
            this.dataGridView1.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.dataGridView1.Name = "dataGridView1";
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            dataGridViewCellStyle3.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView1.RowHeadersDefaultCellStyle = dataGridViewCellStyle3;
            this.dataGridView1.Size = new System.Drawing.Size(891, 590);
            this.dataGridView1.TabIndex = 2;
            this.dataGridView1.CellClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridView1_CellClick);
            this.dataGridView1.SelectionChanged += new System.EventHandler(this.dataGridView1_SelectionChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.BackColor = System.Drawing.Color.Transparent;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label1.ForeColor = System.Drawing.Color.White;
            this.label1.Location = new System.Drawing.Point(976, 110);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(62, 18);
            this.label1.TabIndex = 3;
            this.label1.Text = "Film ID :";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.BackColor = System.Drawing.Color.Transparent;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label2.ForeColor = System.Drawing.Color.White;
            this.label2.Location = new System.Drawing.Point(1008, 150);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(33, 18);
            this.label2.TabIndex = 4;
            this.label2.Text = "Ad :";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.BackColor = System.Drawing.Color.Transparent;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label3.ForeColor = System.Drawing.Color.White;
            this.label3.Location = new System.Drawing.Point(1007, 191);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(34, 18);
            this.label3.TabIndex = 5;
            this.label3.Text = "Tur:";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.BackColor = System.Drawing.Color.Transparent;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label4.ForeColor = System.Drawing.Color.White;
            this.label4.Location = new System.Drawing.Point(1019, 225);
            this.label4.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(31, 18);
            this.label4.TabIndex = 6;
            this.label4.Text = "Yıl :";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.BackColor = System.Drawing.Color.Transparent;
            this.label7.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label7.ForeColor = System.Drawing.Color.White;
            this.label7.Location = new System.Drawing.Point(951, 258);
            this.label7.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(83, 18);
            this.label7.TabIndex = 9;
            this.label7.Text = "Yönetmen :";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.BackColor = System.Drawing.Color.Transparent;
            this.label10.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label10.ForeColor = System.Drawing.Color.White;
            this.label10.Location = new System.Drawing.Point(924, 290);
            this.label10.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(103, 18);
            this.label10.TabIndex = 12;
            this.label10.Text = "Günlük Ücret :";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.BackColor = System.Drawing.Color.Transparent;
            this.label11.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label11.ForeColor = System.Drawing.Color.White;
            this.label11.Location = new System.Drawing.Point(979, 334);
            this.label11.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(61, 18);
            this.label11.TabIndex = 13;
            this.label11.Text = "Durum :";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.BackColor = System.Drawing.Color.Transparent;
            this.label12.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label12.ForeColor = System.Drawing.Color.White;
            this.label12.Location = new System.Drawing.Point(936, 377);
            this.label12.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(93, 18);
            this.label12.TabIndex = 14;
            this.label12.Text = "Resim Linki :";
            // 
            // txtID
            // 
            this.txtID.BackColor = System.Drawing.Color.White;
            this.txtID.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtID.Enabled = false;
            this.txtID.Location = new System.Drawing.Point(1073, 110);
            this.txtID.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtID.Name = "txtID";
            this.txtID.Size = new System.Drawing.Size(194, 22);
            this.txtID.TabIndex = 15;
            // 
            // txtAd
            // 
            this.txtAd.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtAd.Location = new System.Drawing.Point(1073, 153);
            this.txtAd.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtAd.Name = "txtAd";
            this.txtAd.Size = new System.Drawing.Size(194, 22);
            this.txtAd.TabIndex = 16;
            // 
            // txtTur
            // 
            this.txtTur.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtTur.Location = new System.Drawing.Point(1073, 188);
            this.txtTur.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtTur.Name = "txtTur";
            this.txtTur.Size = new System.Drawing.Size(194, 22);
            this.txtTur.TabIndex = 17;
            // 
            // txtYonetmen
            // 
            this.txtYonetmen.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtYonetmen.Location = new System.Drawing.Point(1073, 258);
            this.txtYonetmen.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtYonetmen.Name = "txtYonetmen";
            this.txtYonetmen.Size = new System.Drawing.Size(194, 22);
            this.txtYonetmen.TabIndex = 21;
            // 
            // txtUcret
            // 
            this.txtUcret.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtUcret.Location = new System.Drawing.Point(1073, 290);
            this.txtUcret.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtUcret.Name = "txtUcret";
            this.txtUcret.Size = new System.Drawing.Size(194, 22);
            this.txtUcret.TabIndex = 24;
            // 
            // txtDurum
            // 
            this.txtDurum.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtDurum.Enabled = false;
            this.txtDurum.Location = new System.Drawing.Point(1073, 335);
            this.txtDurum.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtDurum.Name = "txtDurum";
            this.txtDurum.Size = new System.Drawing.Size(194, 22);
            this.txtDurum.TabIndex = 25;
            // 
            // txtLink
            // 
            this.txtLink.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtLink.Location = new System.Drawing.Point(1073, 377);
            this.txtLink.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtLink.Name = "txtLink";
            this.txtLink.Size = new System.Drawing.Size(194, 22);
            this.txtLink.TabIndex = 26;
            // 
            // btnFilmSil
            // 
            this.btnFilmSil.ForeColor = System.Drawing.Color.Black;
            this.btnFilmSil.Location = new System.Drawing.Point(1308, 620);
            this.btnFilmSil.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnFilmSil.Name = "btnFilmSil";
            this.btnFilmSil.Size = new System.Drawing.Size(280, 64);
            this.btnFilmSil.TabIndex = 28;
            this.btnFilmSil.Text = "Filmi Sil";
            this.btnFilmSil.UseVisualStyleBackColor = true;
            this.btnFilmSil.Click += new System.EventHandler(this.btnAracSil_Click);
            // 
            // btnGuncelle
            // 
            this.btnGuncelle.Enabled = false;
            this.btnGuncelle.ForeColor = System.Drawing.Color.Black;
            this.btnGuncelle.Location = new System.Drawing.Point(988, 620);
            this.btnGuncelle.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnGuncelle.Name = "btnGuncelle";
            this.btnGuncelle.Size = new System.Drawing.Size(280, 64);
            this.btnGuncelle.TabIndex = 29;
            this.btnGuncelle.Text = "Filmi Güncelle";
            this.btnGuncelle.UseVisualStyleBackColor = true;
            this.btnGuncelle.Click += new System.EventHandler(this.btnGuncelle_Click_1);
            // 
            // btnFilmEkle
            // 
            this.btnFilmEkle.ForeColor = System.Drawing.Color.Black;
            this.btnFilmEkle.Location = new System.Drawing.Point(988, 15);
            this.btnFilmEkle.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnFilmEkle.Name = "btnFilmEkle";
            this.btnFilmEkle.Size = new System.Drawing.Size(280, 64);
            this.btnFilmEkle.TabIndex = 31;
            this.btnFilmEkle.Text = "Film Ekle";
            this.btnFilmEkle.UseVisualStyleBackColor = true;
            this.btnFilmEkle.Click += new System.EventHandler(this.btnAracEkle_Click);
            // 
            // btnPanelCikis
            // 
            this.btnPanelCikis.ForeColor = System.Drawing.Color.Black;
            this.btnPanelCikis.Location = new System.Drawing.Point(1416, 15);
            this.btnPanelCikis.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnPanelCikis.Name = "btnPanelCikis";
            this.btnPanelCikis.Size = new System.Drawing.Size(280, 64);
            this.btnPanelCikis.TabIndex = 32;
            this.btnPanelCikis.Text = "Panelden Çıkış Yap";
            this.btnPanelCikis.UseVisualStyleBackColor = true;
            this.btnPanelCikis.Click += new System.EventHandler(this.btnPanelCikis_Click);
            // 
            // errorProvider1
            // 
            this.errorProvider1.ContainerControl = this;
            // 
            // cmbYil
            // 
            this.cmbYil.FormattingEnabled = true;
            this.cmbYil.Location = new System.Drawing.Point(1073, 225);
            this.cmbYil.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.cmbYil.Name = "cmbYil";
            this.cmbYil.Size = new System.Drawing.Size(100, 24);
            this.cmbYil.TabIndex = 36;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Location = new System.Drawing.Point(1293, 110);
            this.pictureBox1.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(379, 486);
            this.pictureBox1.TabIndex = 30;
            this.pictureBox1.TabStop = false;
            // 
            // btnKiralanlar
            // 
            this.btnKiralanlar.ForeColor = System.Drawing.Color.Black;
            this.btnKiralanlar.Location = new System.Drawing.Point(592, 15);
            this.btnKiralanlar.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnKiralanlar.Name = "btnKiralanlar";
            this.btnKiralanlar.Size = new System.Drawing.Size(280, 64);
            this.btnKiralanlar.TabIndex = 37;
            this.btnKiralanlar.Text = "Kiralama Paneli";
            this.btnKiralanlar.UseVisualStyleBackColor = true;
            this.btnKiralanlar.Click += new System.EventHandler(this.btnKiralanlar_Click);
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.BackColor = System.Drawing.Color.Transparent;
            this.label8.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.label8.ForeColor = System.Drawing.Color.White;
            this.label8.Location = new System.Drawing.Point(961, 426);
            this.label8.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(74, 18);
            this.label8.TabIndex = 38;
            this.label8.Text = "Hakkında:";
            // 
            // txtHakkinda
            // 
            this.txtHakkinda.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtHakkinda.Location = new System.Drawing.Point(1068, 426);
            this.txtHakkinda.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtHakkinda.Multiline = true;
            this.txtHakkinda.Name = "txtHakkinda";
            this.txtHakkinda.Size = new System.Drawing.Size(194, 169);
            this.txtHakkinda.TabIndex = 39;
            // 
            // lblAdet
            // 
            this.lblAdet.AutoSize = true;
            this.lblAdet.BackColor = System.Drawing.Color.Transparent;
            this.lblAdet.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(162)));
            this.lblAdet.ForeColor = System.Drawing.Color.White;
            this.lblAdet.Location = new System.Drawing.Point(1180, 227);
            this.lblAdet.Name = "lblAdet";
            this.lblAdet.Size = new System.Drawing.Size(44, 18);
            this.lblAdet.TabIndex = 40;
            this.lblAdet.Text = "Adet :";
            // 
            // nudAdet
            // 
            this.nudAdet.Location = new System.Drawing.Point(1227, 225);
            this.nudAdet.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.nudAdet.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudAdet.Name = "nudAdet";
            this.nudAdet.Size = new System.Drawing.Size(40, 22);
            this.nudAdet.TabIndex = 41;
            this.nudAdet.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // chkKiralamayaAcik
            // 
            this.chkKiralamayaAcik.AutoSize = true;
            this.chkKiralamayaAcik.BackColor = System.Drawing.Color.Transparent;
            this.chkKiralamayaAcik.Checked = true;
            this.chkKiralamayaAcik.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkKiralamayaAcik.ForeColor = System.Drawing.Color.White;
            this.chkKiralamayaAcik.Location = new System.Drawing.Point(1293, 598);
            this.chkKiralamayaAcik.Name = "chkKiralamayaAcik";
            this.chkKiralamayaAcik.Size = new System.Drawing.Size(137, 20);
            this.chkKiralamayaAcik.TabIndex = 42;
            this.chkKiralamayaAcik.Text = "Kiralamaya açık";
            this.chkKiralamayaAcik.UseVisualStyleBackColor = false;
            // 
            // AdminPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1699, 699);
            this.Controls.Add(this.chkKiralamayaAcik);
            this.Controls.Add(this.nudAdet);
            this.Controls.Add(this.lblAdet);
            this.Controls.Add(this.txtHakkinda);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.btnKiralanlar);
            this.Controls.Add(this.cmbYil);
            this.Controls.Add(this.btnPanelCikis);
            this.Controls.Add(this.btnFilmEkle);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.btnGuncelle);
            this.Controls.Add(this.btnFilmSil);
            this.Controls.Add(this.txtLink);
            this.Controls.Add(this.txtDurum);
            this.Controls.Add(this.txtUcret);
            this.Controls.Add(this.txtYonetmen);
            this.Controls.Add(this.txtTur);
            this.Controls.Add(this.txtAd);
            this.Controls.Add(this.txtID);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.dataGridView1);
            this.Controls.Add(this.btnKullaniciPanel);
            this.Controls.Add(this.btnAracDurum);
            this.ForeColor = System.Drawing.Color.White;
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "AdminPanel";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Yönetim Paneli";
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.errorProvider1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudAdet)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnAracDurum;
        private System.Windows.Forms.Button btnKullaniciPanel;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.TextBox txtID;
        private System.Windows.Forms.TextBox txtAd;
        private System.Windows.Forms.TextBox txtTur;
        private System.Windows.Forms.TextBox txtYonetmen;
        private System.Windows.Forms.TextBox txtUcret;
        private System.Windows.Forms.TextBox txtDurum;
        private System.Windows.Forms.TextBox txtLink;
        private System.Windows.Forms.Button btnFilmSil;
        private System.Windows.Forms.Button btnGuncelle;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Button btnFilmEkle;
        private System.Windows.Forms.Button btnPanelCikis;
        private System.Windows.Forms.ErrorProvider errorProvider1;
        private System.Windows.Forms.ComboBox cmbYil;
        private System.Windows.Forms.Button btnKiralanlar;
        private System.Windows.Forms.TextBox txtHakkinda;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label lblAdet;
        private System.Windows.Forms.NumericUpDown nudAdet;
        private System.Windows.Forms.CheckBox chkKiralamayaAcik;
    }
}

