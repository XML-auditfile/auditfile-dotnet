namespace XmlAuditfile;

// Version names follow the registry: type name and version with underscores, e.g. V3_2 for 3.2.
#pragma warning disable CA1707

// Nested static classes per audit file type are deliberate: AuditfileVersion.Financial.V3_2 reads as what it is.
#pragma warning disable CA1034

// The version constants. A test checks that every registry entry has a constant here and vice versa.
public sealed partial class AuditfileVersion
{
    /// <summary>Auditfile Financieel (XAF).</summary>
    public static class Financial
    {
        /// <summary>XAF 3.2 (2014, supplemented in 2017).</summary>
        public static AuditfileVersion V3_2 => Parse("xaf/3.2");

        /// <summary>XAF 4.0.3.</summary>
        public static AuditfileVersion V4_0_3 => Parse("xaf/4.0.3");
    }

    /// <summary>Auditfile Afrekensystemen (XAA).</summary>
    public static class CashRegister
    {
        /// <summary>XAA 3.2.</summary>
        public static AuditfileVersion V3_2 => Parse("xaa/3.2");
    }

    /// <summary>Auditfile Boordcomputer Taxi (XAB).</summary>
    public static class TaxiOnboardComputer
    {
        /// <summary>XAB 1.0.</summary>
        public static AuditfileVersion V1_0 => Parse("xab/1.0");
    }

    /// <summary>Auditfile Ritregistratie (XAR).</summary>
    public static class TripRegistration
    {
        /// <summary>XAR 1.2.</summary>
        public static AuditfileVersion V1_2 => Parse("xar/1.2");

        /// <summary>XAR 2.3.6.1.</summary>
        public static AuditfileVersion V2_3_6_1 => Parse("xar/2.3.6.1");
    }

    /// <summary>Auditfile Salaris (XAS).</summary>
    public static class Payroll
    {
        /// <summary>XAS 2018.1.0.</summary>
        public static AuditfileVersion V2018_1_0 => Parse("xas/2018.1.0");

        /// <summary>XAS 2019.1.0.</summary>
        public static AuditfileVersion V2019_1_0 => Parse("xas/2019.1.0");

        /// <summary>XAS 2020.1.0.</summary>
        public static AuditfileVersion V2020_1_0 => Parse("xas/2020.1.0");

        /// <summary>XAS 2021.1.1.</summary>
        public static AuditfileVersion V2021_1_1 => Parse("xas/2021.1.1");

        /// <summary>XAS 2022.1.1.</summary>
        public static AuditfileVersion V2022_1_1 => Parse("xas/2022.1.1");

        /// <summary>XAS 2023.1.0.</summary>
        public static AuditfileVersion V2023_1_0 => Parse("xas/2023.1.0");

        /// <summary>XAS 2024.1.0.</summary>
        public static AuditfileVersion V2024_1_0 => Parse("xas/2024.1.0");

        /// <summary>XAS 2025.1.0.</summary>
        public static AuditfileVersion V2025_1_0 => Parse("xas/2025.1.0");

        /// <summary>XAS 2026.1.0.</summary>
        public static AuditfileVersion V2026_1_0 => Parse("xas/2026.1.0");

        /// <summary>XAS 2027.1.0.</summary>
        public static AuditfileVersion V2027_1_0 => Parse("xas/2027.1.0");
    }
}

#pragma warning restore CA1707
#pragma warning restore CA1034
