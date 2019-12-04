﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewLife.Cube.Extensions;
using NewLife.Log;
using NewLife.Reflection;
using XCode;
using HttpContext = NewLife.Web.HttpContext;

namespace NewLife.Cube
{
    /// <summary>实体模型绑定器</summary>
    public class EntityModelBinder : ComplexTypeModelBinder
    {
        /// <summary>实例化实体模型绑定器</summary>
        /// <param name="propertyBinders"></param>
        /// <param name="loggerFactory"></param>
        public EntityModelBinder(IDictionary<ModelMetadata, IModelBinder> propertyBinders, ILoggerFactory loggerFactory)
            : base(propertyBinders, loggerFactory)
        {
            _propertyBinders = propertyBinders ?? throw new ArgumentNullException(nameof(propertyBinders));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        private readonly IDictionary<ModelMetadata, IModelBinder> _propertyBinders;
        private readonly ILoggerFactory _loggerFactory;

        ///// <summary>异步绑定模型</summary>
        ///// <param name="bindingContext"></param>
        ///// <returns></returns>
        //public async Task BindModelAsync(ModelBindingContext bindingContext)
        //{
        //    var modelType = bindingContext.ModelType;
        //    var controllerContext = bindingContext.ActionContext;
        //    if (modelType.As<IEntity>())
        //    {
        //        var fact = EntityFactory.CreateOperate(modelType);
        //        if (fact != null)
        //        {
        //            var rvs = controllerContext.RouteData.Values;
        //            var pks = fact.Table.PrimaryKeys;
        //            var uk = fact.Unique;

        //            IEntity entity = null;
        //            if (uk != null)
        //            {
        //                // 查询实体对象用于编辑
        //                if (rvs[uk.Name] != null) entity =
        //                    //GetEntity(fact.EntityType, rvs[uk.Name]) ?? 
        //                    fact.FindByKeyForEdit(rvs[uk.Name]); // 从session取回来的实体全部被设置了脏属性，每次保存所有数据，因此从数据查找
        //                if (entity == null) entity = fact.Create();
        //            }
        //            else if (pks.Length > 0)
        //            {
        //                // 查询实体对象用于编辑
        //                var vs = pks.Select(e => rvs[e.Name]).ToArray();
        //                entity = GetEntity(fact.EntityType, vs);
        //                if (entity == null)
        //                {
        //                    var req = controllerContext.HttpContext.Request;
        //                    var exp = new WhereExpression();
        //                    foreach (var item in pks)
        //                    {
        //                        exp &= item.Equal(req.GetRequestValue(item.Name).ChangeType(item.Type));
        //                    }

        //                    entity = fact.Find(exp);
        //                }
        //                if (entity == null) entity = fact.Create();
        //            }

        //            if (entity != null)
        //            {
        //                var req = controllerContext.HttpContext.Request;
        //                if (req.HasFormContentType)
        //                {
        //                    var fs = req.Form;
        //                    // 提前填充动态字段的扩展属性
        //                    foreach (var item in fact.Fields)
        //                    {
        //                        if (item.IsDynamic && fs.ContainsKey(item.Name)) entity.SetItem(item.Name, fs[item.Name]);
        //                    }
        //                }

        //                bindingContext.Result = ModelBindingResult.Success(entity);
        //            }

        //            if (entity == null)
        //            {
        //                bindingContext.Result = ModelBindingResult.Success(fact.Create());
        //            }

        //            // 为Model赋值，为下面BindProperty方法做准备
        //            bindingContext.Model = bindingContext.Result.Model;

        //            // 使用复杂类型模型绑定器ComplexTypeModelBinder填充Model
        //            await BindProperty(bindingContext);
        //        }
        //    }

        //    //return Task.CompletedTask;
        //}

        /// <summary>创建模型。对于有Key的请求，使用FindByKeyForEdit方法先查出来数据，而不是直接反射实例化实体对象</summary>
        /// <param name="bindingContext"></param>
        /// <returns></returns>
        protected override Object CreateModel(ModelBindingContext bindingContext)
        {
            var modelType = bindingContext.ModelType;
            if (modelType.As<IEntity>())
            {
                var fact = EntityFactory.CreateOperate(modelType);
                if (fact != null)
                {
                    var rvs = bindingContext.ActionContext.RouteData.Values;
                    var pks = fact.Table.PrimaryKeys;
                    var uk = fact.Unique;

                    IEntity entity = null;
                    if (uk != null)
                    {
                        // 查询实体对象用于编辑
                        var id = rvs[uk.Name];
                        if (id != null) entity = GetEntity(fact.EntityType, id) ?? fact.FindByKeyForEdit(id);
                        if (entity == null) entity = fact.Create();
                    }
                    else if (pks.Length > 0)
                    {
                        // 查询实体对象用于编辑
                        var vs = pks.Select(e => rvs[e.Name]).ToArray();
                        entity = GetEntity(fact.EntityType, vs);
                        if (entity == null)
                        {
                            var req = bindingContext.HttpContext.Request.Query;
                            var exp = new WhereExpression();
                            foreach (var item in pks)
                            {
                                exp &= item.Equal(req[item.Name].ChangeType(item.Type));
                            }

                            entity = fact.Find(exp);
                        }
                        if (entity == null) entity = fact.Create();
                    }

                    if (entity != null)
                    {
                        var fs = bindingContext.HttpContext.Request.Form;
                        // 提前填充动态字段的扩展属性
                        foreach (var item in fact.Fields)
                        {
                            if (item.IsDynamic && fs.ContainsKey(item.Name)) entity.SetItem(item.Name, fs[item.Name]);
                        }

                        return entity;
                    }

                    return fact.Create();
                }
            }

            return base.CreateModel(bindingContext);
        }

        private static String GetCacheKey(Type type, params Object[] keys)
        {
            return "CubeModel_{0}_{1}".F(type.FullName, keys.Join("_"));
        }

        /// <summary>呈现表单前，保存实体对象。提交时优先使用该对象而不是去数据库查找，避免脏写</summary>
        /// <param name="entity"></param>
        internal static void SetEntity(IEntity entity)
        {
            var ctx = HttpContext.Current;
            var fact = EntityFactory.CreateOperate(entity.GetType());

            var ckey = "";
            var pks = fact.Table.PrimaryKeys;
            var uk = fact.Unique;
            if (uk != null)
                ckey = GetCacheKey(entity.GetType(), entity[uk.Name]);
            else if (pks.Length > 0)
                ckey = GetCacheKey(entity.GetType(), pks.Select(e => entity[e.Name]).ToArray());

            ctx.Session.Set(ckey, entity);
        }

        private static IEntity GetEntity(Type type, params Object[] keys)
        {
            var ctx = HttpContext.Current;
            var ckey = GetCacheKey(type, keys);
            return ctx.Session.Get(ckey, type) as IEntity;
        }

        //private async Task BindProperty(ModelBindingContext bindingContext)
        //{
        //    var complexTypeModelBinder = new ComplexTypeModelBinder(_propertyBinders, _loggerFactory);

        //    await complexTypeModelBinder.BindModelAsync(bindingContext);
        //}
    }

    /// <summary>实体模型绑定器提供者，为所有XCode实体类提供实体模型绑定器</summary>
    public class EntityModelBinderProvider : IModelBinderProvider
    {
        /// <summary>
        /// 获取绑定器
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!context.Metadata.ModelType.As<IEntity>()) return null;
            var propertyBinders = new Dictionary<ModelMetadata, IModelBinder>();
            foreach (var property in context.Metadata.Properties)
            {
                propertyBinders.Add(property, context.CreateBinder(property));
            }

            var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
            return new EntityModelBinder(propertyBinders, loggerFactory);
        }

        static EntityModelBinderProvider()
        {
            XTrace.WriteLine("注册实体模型绑定器：{0}", typeof(EntityModelBinderProvider).FullName);
            //ModelBinderProviders.BinderProviders.Add(new EntityModelBinderProvider());
        }

        /// <summary>注册到全局模型绑定器提供者集合</summary>
        public static void Register()
        {
            // 引发静态构造，只执行一次
        }
    }
}