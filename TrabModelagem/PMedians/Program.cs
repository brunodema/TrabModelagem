﻿using System;
using System.CodeDom.Compiler;
using System.Globalization;
using System.IO.Enumeration;
using System.Linq;
using System.Reflection;
using Gurobi;
using InstanceGenerator;

namespace PMedians
{
    class VariableGenerator
    {
        private readonly InstanceGenerator.InstanceGenerator InstData;
        private Gurobi.GRBModel Model;
        public GRBVar[,] depot_usage;
        public GRBVar[,,] customer_depot_designation;

        public VariableGenerator(InstanceGenerator.InstanceGenerator pInstData, Gurobi.GRBModel pModel)
        {
            InstData = pInstData;
            Model = pModel;
            depot_usage = new GRBVar[InstData.getInstanceConfig().n_depots, InstData.getInstanceConfig().time_periods];
            customer_depot_designation = new GRBVar[InstData.getInstanceConfig().n_nodes, InstData.getInstanceConfig().n_depots, InstData.getInstanceConfig().time_periods];
        }

        private void create_depot_usage_vars()
        {
            for (int j = 0; j < InstData.getInstanceConfig().n_depots; j++)
            {
                for (int t = 0; t < InstData.getInstanceConfig().time_periods; t++)
                {
                    this.depot_usage[j, t] = Model.AddVar(0.00, 1.00, 1.00, GRB.BINARY, String.Format("y_j{0}_t{1}", j, t));
                }
            }
            return;
        }
        private void create_depot_customer_designation_vars()
        {
            for (int j = 0; j < InstData.getInstanceConfig().n_depots; j++)
            {
                for (int i = 0; i < InstData.getInstanceConfig().n_nodes; i++)
                {
                    for (int t = 0; t < InstData.getInstanceConfig().time_periods; t++)
                    {
                        this.customer_depot_designation[i, j, t] = Model.AddVar(0.00, 1.00, 1.00, GRB.BINARY, String.Format("x_i{0}_j{1}_t{2}", i, j, t));
                    }
                }
            }
            return;
        }
        public void make_all_vars()
        {
            this.create_depot_usage_vars();
            this.create_depot_customer_designation_vars();
        }
    }

    class ConstraintGenerator
    {
        private GRBModel Model;
        private readonly VariableGenerator variableGenerator;
        private readonly InstanceGenerator.InstanceGenerator instanceGenerator;

        public ConstraintGenerator(GRBModel pModel, VariableGenerator pvariableGenerator, InstanceGenerator.InstanceGenerator pinstanceGenerator)
        {
            Model = pModel;
            variableGenerator = pvariableGenerator;
            instanceGenerator = pinstanceGenerator;
        }

        public void max_depot_nodes_per_period(int n_DepotNodes)
        {
            for (int t = 0; t < instanceGenerator.getInstanceConfig().time_periods; t++)
            {
                GRBLinExpr sum = 0;
                for (int j = 0; j < instanceGenerator.getInstanceConfig().n_depots; j++)
                {
                    sum += variableGenerator.depot_usage[j, t];
                }
                Model.AddConstr(sum <= instanceGenerator.getInstanceConfig().max_depot_nodes_per_period, String.Format("max_depot_nodes_per_period_t{0}", t));
            }
        }
        public void max_nodes_per_depot(int nodelimit)
        {
            for (int t = 0; t < instanceGenerator.getInstanceConfig().time_periods; t++)
            {
                for (int j = 0; j < instanceGenerator.getInstanceConfig().n_depots; j++)
                {
                    GRBLinExpr sum = 0;
                    for (int i = 0; i < instanceGenerator.getInstanceConfig().n_nodes; i++)
                    {
                        sum += variableGenerator.customer_depot_designation[i, j, t];
                    }
                    Model.AddConstr(sum <= instanceGenerator.getInstanceConfig().max_nodes_per_depot, String.Format("max_nodes_per_depot_j{0}_t{1}", j, t));
                }
            }
        }
        public void one_visit_per_node()
        {
            for (int i = 0; i < instanceGenerator.getInstanceConfig().n_nodes; i++)
            {
                GRBLinExpr sum = 0;
                for (int t = 0; t < instanceGenerator.getInstanceConfig().time_periods; t++)
                {
                    for (int j = 0; j < instanceGenerator.getInstanceConfig().n_depots; j++)
                    {
                        sum += variableGenerator.customer_depot_designation[i, j, t];
                    }
                }
                Model.AddConstr(sum == 1, String.Format("one_visit_per_node_i{0}", i));
            }
        }
        public void service_only_by_active_depot()
        {
            for (int t = 0; t < instanceGenerator.getInstanceConfig().time_periods; t++)
            {
                for (int j = 0; j < instanceGenerator.getInstanceConfig().n_depots; j++)
                {
                    GRBLinExpr sum = 0;
                    for (int i = 0; i < instanceGenerator.getInstanceConfig().n_nodes; i++)
                    {
                        sum += variableGenerator.customer_depot_designation[i, j, t];
                    }
                    Model.AddConstr(sum <= variableGenerator.depot_usage[j, t] * instanceGenerator.getInstanceConfig().max_nodes_per_depot, String.Format("service_only_by_active_depot_j{0}_t{1}", j, t));
                }
            }
        }

        public void make_all_constraints()
        {
            this.max_depot_nodes_per_period(instanceGenerator.getInstanceConfig().max_depot_nodes_per_period);
            //this.max_nodes_per_depot(instanceGenerator.getInstanceConfig().max_nodes_per_depot);
            this.one_visit_per_node();
            this.service_only_by_active_depot();

            this.setup_objective();
        }

        public void setup_objective()
        {
            GRBLinExpr sum_depot_expr = 0;
            GRBLinExpr sum_node_expr = 0;
            for (int t = 0; t < instanceGenerator.getInstanceConfig().time_periods; t++)
            {
                for (int j = 0; j < instanceGenerator.getInstanceConfig().n_depots; j++)
                {
                    sum_depot_expr += variableGenerator.depot_usage[j, t] * instanceGenerator.getInstanceConfig().depot_usage_cost;
                    for (int i = 0; i < instanceGenerator.getInstanceConfig().n_nodes; i++)
                    {
                        sum_node_expr += variableGenerator.customer_depot_designation[i, j, t] * instanceGenerator.customer_depot_assignment_cost[i, j];
                    }
                }
            }
            Model.SetObjective(sum_depot_expr + sum_node_expr, GRB.MINIMIZE);
        }
    }

    class PMedianConfig
    {
        // to do
    }

    class PMedian
    {
        private readonly InstanceGenerator.InstanceGenerator Instance;
        private Gurobi.GRBEnv env;
        private Gurobi.GRBModel Model;
        private string filename;

        public PMedian(InstanceGenerator.InstanceGenerator pInstance, string pfilename = "PMEDIAN.log")
        {
            Instance = pInstance;
            filename = pfilename;
        }

        private void setup_env()
        {
            env = new GRBEnv(filename);
        }
        private void setup_model()
        {
            Model = new GRBModel(env);
        }

        public void setup(string filename = "")
        {
            this.setup_env();
            this.setup_model();
        }

        public void setup_problem()
        {
            VariableGenerator variableGenerator = new VariableGenerator(Instance, Model);
            variableGenerator.make_all_vars();
            ConstraintGenerator constraintGenerator = new ConstraintGenerator(Model, variableGenerator, Instance);
            constraintGenerator.make_all_constraints();
        }

        public void draw_instance()
        {
            // to do
        }
        public int solve_instance()
        {
            this.Model.Optimize();
            if (this.Model.Status == GRB.Status.INFEASIBLE)
            {
                this.IIS();
            }
            return 0;
        }

        private void IIS()
        {
            this.Model.ComputeIIS();
            this.Model.Write("Infeasible.ilp");
        }

        public void write_lp()
        {
            this.Model.Write(filename + ".lp");
        }

        public void write_sol()
        {
            this.Model.Write(filename + ".sol");
        }

        public void publish_model()
        {
            this.write_lp();
            this.write_sol();
        }

        public void draw_solution()
        {
            // to do
        }
    }


    class main
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            InstanceGenerator.InstanceGenerator instanceGenerator = new InstanceGenerator.InstanceGenerator(new InstanceGeneratorConfig());
            instanceGenerator.create_instance();
            PMedian pMedianProblem = new PMedian(instanceGenerator, "PMedian.log");
            pMedianProblem.setup();
            pMedianProblem.setup_problem();
            pMedianProblem.solve_instance();
            pMedianProblem.publish_model();
        }
    }
}
