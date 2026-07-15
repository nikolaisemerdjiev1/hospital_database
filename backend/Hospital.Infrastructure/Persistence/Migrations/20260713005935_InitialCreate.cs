using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hospital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        private static readonly string[] AppointmentSlotStatusColumns =
            ["availability_slot_id", "status"];

        private static readonly string[] AppointmentPatientStatusColumns =
            ["patient_profile_id", "status"];

        private static readonly string[] AuditActorOccurredAtColumns =
            ["actor_user_profile_id", "occurred_at_utc"];

        private static readonly string[] AuditEntityOccurredAtColumns =
            ["affected_entity_type", "affected_entity_id", "occurred_at_utc"];

        private static readonly string[] AvailabilitySlotClinicianStartColumns =
            ["clinician_profile_id", "starts_at_utc"];

        private static readonly string[] FulfillmentStatusCreatedAtColumns =
            ["status", "created_at_utc"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medication",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    rx_cui = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    strength = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    dose_form = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    classification = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("medication_pkey", x => x.id);
                    table.CheckConstraint("medication_display_name_check", "length(btrim(display_name)) > 0");
                    table.CheckConstraint("medication_rx_cui_check", "length(btrim(rx_cui)) > 0");
                    table.CheckConstraint("medication_source_check", "source IN ('RxNorm', 'SeededFallback')");
                });

            migrationBuilder.CreateTable(
                name: "user_profile",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    auth0_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    profile_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    account_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("user_profile_pkey", x => x.id);
                    table.CheckConstraint("user_profile_account_status_check", "account_status IN ('Active', 'Inactive')");
                    table.CheckConstraint("user_profile_auth0_subject_check", "length(btrim(auth0_subject)) > 0");
                    table.CheckConstraint("user_profile_display_name_check", "length(btrim(display_name)) > 0");
                    table.CheckConstraint("user_profile_profile_type_check", "profile_type IN ('Patient', 'Doctor', 'Pharmacist', 'Administrator')");
                });

            migrationBuilder.CreateTable(
                name: "audit_event",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    actor_user_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    affected_entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    affected_entity_id = table.Column<long>(type: "bigint", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("audit_event_pkey", x => x.id);
                    table.CheckConstraint("audit_event_action_check", "length(btrim(action)) > 0");
                    table.CheckConstraint("audit_event_affected_entity_type_check", "length(btrim(affected_entity_type)) > 0");
                    table.CheckConstraint("audit_event_metadata_size_check", "metadata_json IS NULL OR length(metadata_json::text) <= 4000");
                    table.ForeignKey(
                        name: "audit_event_actor_user_profile_id_fkey",
                        column: x => x.actor_user_profile_id,
                        principalTable: "user_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "clinician_profile",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    staff_identifier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    specialty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("clinician_profile_pkey", x => x.id);
                    table.CheckConstraint("clinician_profile_specialty_check", "length(btrim(specialty)) > 0");
                    table.CheckConstraint("clinician_profile_staff_identifier_check", "length(btrim(staff_identifier)) > 0");
                    table.ForeignKey(
                        name: "clinician_profile_user_profile_id_fkey",
                        column: x => x.user_profile_id,
                        principalTable: "user_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_profile",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    medical_record_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    allergy_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("patient_profile_pkey", x => x.id);
                    table.CheckConstraint("patient_profile_medical_record_number_check", "length(btrim(medical_record_number)) > 0");
                    table.ForeignKey(
                        name: "patient_profile_user_profile_id_fkey",
                        column: x => x.user_profile_id,
                        principalTable: "user_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pharmacist_profile",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    staff_identifier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pharmacy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pharmacist_profile_pkey", x => x.id);
                    table.CheckConstraint("pharmacist_profile_pharmacy_name_check", "length(btrim(pharmacy_name)) > 0");
                    table.CheckConstraint("pharmacist_profile_staff_identifier_check", "length(btrim(staff_identifier)) > 0");
                    table.ForeignKey(
                        name: "pharmacist_profile_user_profile_id_fkey",
                        column: x => x.user_profile_id,
                        principalTable: "user_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "availability_slot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    clinician_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("availability_slot_pkey", x => x.id);
                    table.CheckConstraint("availability_slot_time_range_check", "ends_at_utc > starts_at_utc");
                    table.ForeignKey(
                        name: "availability_slot_clinician_profile_id_fkey",
                        column: x => x.clinician_profile_id,
                        principalTable: "clinician_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    patient_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    availability_slot_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("appointment_pkey", x => x.id);
                    table.CheckConstraint("appointment_cancellation_check", "(status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND cancelled_at_utc >= created_at_utc) OR (status <> 'Cancelled' AND cancelled_at_utc IS NULL AND cancellation_reason IS NULL)");
                    table.CheckConstraint("appointment_reason_check", "length(btrim(reason)) > 0");
                    table.CheckConstraint("appointment_status_check", "status IN ('Scheduled', 'InProgress', 'Completed', 'Cancelled', 'NoShow')");
                    table.ForeignKey(
                        name: "appointment_availability_slot_id_fkey",
                        column: x => x.availability_slot_id,
                        principalTable: "availability_slot",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "appointment_patient_profile_id_fkey",
                        column: x => x.patient_profile_id,
                        principalTable: "patient_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consultation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    appointment_id = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    clinical_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    patient_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    care_instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("consultation_pkey", x => x.id);
                    table.CheckConstraint("consultation_completion_check", "(status = 'Draft' AND completed_at_utc IS NULL) OR (status = 'Completed' AND completed_at_utc IS NOT NULL AND completed_at_utc >= started_at_utc AND outcome IS NOT NULL AND length(btrim(outcome)) > 0 AND clinical_notes IS NOT NULL AND length(btrim(clinical_notes)) > 0 AND patient_summary IS NOT NULL AND length(btrim(patient_summary)) > 0 AND care_instructions IS NOT NULL AND length(btrim(care_instructions)) > 0)");
                    table.CheckConstraint("consultation_status_check", "status IN ('Draft', 'Completed')");
                    table.ForeignKey(
                        name: "consultation_appointment_id_fkey",
                        column: x => x.appointment_id,
                        principalTable: "appointment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prescription",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    consultation_id = table.Column<long>(type: "bigint", nullable: false),
                    medication_id = table.Column<long>(type: "bigint", nullable: false),
                    prescriber_clinician_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    patient_profile_id = table.Column<long>(type: "bigint", nullable: false),
                    rx_cui_snapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    medication_display_name_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("prescription_pkey", x => x.id);
                    table.CheckConstraint("prescription_cancellation_check", "(status = 'Issued' AND cancelled_at_utc IS NULL) OR (status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND cancelled_at_utc >= issued_at_utc)");
                    table.CheckConstraint("prescription_dose_check", "length(btrim(dose)) > 0");
                    table.CheckConstraint("prescription_instructions_check", "length(btrim(instructions)) > 0");
                    table.CheckConstraint("prescription_medication_name_snapshot_check", "length(btrim(medication_display_name_snapshot)) > 0");
                    table.CheckConstraint("prescription_quantity_check", "quantity > 0");
                    table.CheckConstraint("prescription_rx_cui_snapshot_check", "length(btrim(rx_cui_snapshot)) > 0");
                    table.CheckConstraint("prescription_status_check", "status IN ('Issued', 'Cancelled')");
                    table.ForeignKey(
                        name: "prescription_consultation_id_fkey",
                        column: x => x.consultation_id,
                        principalTable: "consultation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "prescription_medication_id_fkey",
                        column: x => x.medication_id,
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "prescription_patient_profile_id_fkey",
                        column: x => x.patient_profile_id,
                        principalTable: "patient_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "prescription_prescriber_clinician_profile_id_fkey",
                        column: x => x.prescriber_clinician_profile_id,
                        principalTable: "clinician_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    prescription_id = table.Column<long>(type: "bigint", nullable: false),
                    assigned_pharmacist_profile_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    review_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ready_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispensed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("fulfillment_pkey", x => x.id);
                    table.CheckConstraint("fulfillment_assignment_check", "(assigned_pharmacist_profile_id IS NULL) = (review_started_at_utc IS NULL)");
                    table.CheckConstraint("fulfillment_state_check", "(status = 'Pending' AND assigned_pharmacist_profile_id IS NULL AND review_started_at_utc IS NULL AND ready_at_utc IS NULL AND dispensed_at_utc IS NULL AND cancelled_at_utc IS NULL) OR (status = 'InReview' AND assigned_pharmacist_profile_id IS NOT NULL AND review_started_at_utc IS NOT NULL AND ready_at_utc IS NULL AND dispensed_at_utc IS NULL AND cancelled_at_utc IS NULL) OR (status = 'Ready' AND assigned_pharmacist_profile_id IS NOT NULL AND review_started_at_utc IS NOT NULL AND ready_at_utc IS NOT NULL AND dispensed_at_utc IS NULL AND cancelled_at_utc IS NULL) OR (status = 'Dispensed' AND assigned_pharmacist_profile_id IS NOT NULL AND review_started_at_utc IS NOT NULL AND ready_at_utc IS NOT NULL AND dispensed_at_utc IS NOT NULL AND cancelled_at_utc IS NULL) OR (status = 'Cancelled' AND cancelled_at_utc IS NOT NULL AND dispensed_at_utc IS NULL)");
                    table.CheckConstraint("fulfillment_status_check", "status IN ('Pending', 'InReview', 'Ready', 'Dispensed', 'Cancelled')");
                    table.CheckConstraint("fulfillment_timestamp_order_check", "(review_started_at_utc IS NULL OR review_started_at_utc >= created_at_utc) AND (ready_at_utc IS NULL OR (review_started_at_utc IS NOT NULL AND ready_at_utc >= review_started_at_utc)) AND (dispensed_at_utc IS NULL OR (ready_at_utc IS NOT NULL AND dispensed_at_utc >= ready_at_utc)) AND (cancelled_at_utc IS NULL OR cancelled_at_utc >= COALESCE(ready_at_utc, review_started_at_utc, created_at_utc))");
                    table.ForeignKey(
                        name: "fulfillment_assigned_pharmacist_profile_id_fkey",
                        column: x => x.assigned_pharmacist_profile_id,
                        principalTable: "pharmacist_profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fulfillment_prescription_id_fkey",
                        column: x => x.prescription_id,
                        principalTable: "prescription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "appointment_active_slot_unique",
                table: "appointment",
                column: "availability_slot_id",
                unique: true,
                filter: "status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "appointment_availability_slot_status_idx",
                table: "appointment",
                columns: AppointmentSlotStatusColumns);

            migrationBuilder.CreateIndex(
                name: "appointment_patient_profile_status_idx",
                table: "appointment",
                columns: AppointmentPatientStatusColumns);

            migrationBuilder.CreateIndex(
                name: "audit_event_actor_occurred_at_utc_idx",
                table: "audit_event",
                columns: AuditActorOccurredAtColumns);

            migrationBuilder.CreateIndex(
                name: "audit_event_affected_entity_occurred_at_utc_idx",
                table: "audit_event",
                columns: AuditEntityOccurredAtColumns);

            migrationBuilder.CreateIndex(
                name: "availability_slot_clinician_start_unique",
                table: "availability_slot",
                columns: AvailabilitySlotClinicianStartColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "clinician_profile_staff_identifier_unique",
                table: "clinician_profile",
                column: "staff_identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "clinician_profile_user_profile_id_unique",
                table: "clinician_profile",
                column: "user_profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "consultation_appointment_id_unique",
                table: "consultation",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "fulfillment_assigned_pharmacist_profile_id_idx",
                table: "fulfillment",
                column: "assigned_pharmacist_profile_id");

            migrationBuilder.CreateIndex(
                name: "fulfillment_prescription_id_unique",
                table: "fulfillment",
                column: "prescription_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "fulfillment_status_created_at_utc_idx",
                table: "fulfillment",
                columns: FulfillmentStatusCreatedAtColumns);

            migrationBuilder.CreateIndex(
                name: "medication_rx_cui_unique",
                table: "medication",
                column: "rx_cui",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "patient_profile_medical_record_number_unique",
                table: "patient_profile",
                column: "medical_record_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "patient_profile_user_profile_id_unique",
                table: "patient_profile",
                column: "user_profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "pharmacist_profile_staff_identifier_unique",
                table: "pharmacist_profile",
                column: "staff_identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "pharmacist_profile_user_profile_id_unique",
                table: "pharmacist_profile",
                column: "user_profile_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "prescription_consultation_id_idx",
                table: "prescription",
                column: "consultation_id");

            migrationBuilder.CreateIndex(
                name: "prescription_medication_id_idx",
                table: "prescription",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "prescription_patient_profile_id_idx",
                table: "prescription",
                column: "patient_profile_id");

            migrationBuilder.CreateIndex(
                name: "prescription_prescriber_clinician_profile_id_idx",
                table: "prescription",
                column: "prescriber_clinician_profile_id");

            migrationBuilder.CreateIndex(
                name: "user_profile_auth0_subject_unique",
                table: "user_profile",
                column: "auth0_subject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_event");

            migrationBuilder.DropTable(
                name: "fulfillment");

            migrationBuilder.DropTable(
                name: "pharmacist_profile");

            migrationBuilder.DropTable(
                name: "prescription");

            migrationBuilder.DropTable(
                name: "consultation");

            migrationBuilder.DropTable(
                name: "medication");

            migrationBuilder.DropTable(
                name: "appointment");

            migrationBuilder.DropTable(
                name: "availability_slot");

            migrationBuilder.DropTable(
                name: "patient_profile");

            migrationBuilder.DropTable(
                name: "clinician_profile");

            migrationBuilder.DropTable(
                name: "user_profile");
        }
    }
}
